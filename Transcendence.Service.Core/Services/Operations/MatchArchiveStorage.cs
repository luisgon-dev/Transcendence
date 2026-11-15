using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.Operations;

public sealed class MatchArchiveOptions
{
    public bool Enabled { get; set; }
    public string Directory { get; set; } = "/var/lib/transcendence/archive";
    public string MaintenanceDirectory { get; set; } = "/var/lib/transcendence/maintenance";
    public int KeepPatches { get; set; } = 3;
    public int FreezeMatches { get; set; } = 250;
    public int DeleteBatch { get; set; } = 25;
    public int MaxRunSeconds { get; set; } = 1200;
    public int CommandTimeoutSeconds { get; set; } = 120;
    public int DeleteTimeoutSeconds { get; set; } = 15;
    public int BatchDelayMilliseconds { get; set; } = 2000;
    public int MaxWalMegabytesPerRun { get; set; } = 256;
    public int MaxIoPressure { get; set; } = 35;
    public string IoPressureFile { get; set; } = "/proc/pressure/io";
}

public interface IMatchArchiveDestination
{
    void Verify(string directory);
}

/// <summary>A bind of an empty local directory must never masquerade as the NAS.</summary>
public sealed class NetworkMatchArchiveDestination : IMatchArchiveDestination
{
    public void Verify(string directory)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("Managed archive storage requires a Linux NFS/SMB mount.");
        if (!IsNetworkMount(directory, File.ReadLines("/proc/self/mountinfo")))
            throw new InvalidOperationException("Archive destination is not a mounted NFS/SMB filesystem.");
    }

    internal static bool IsNetworkMount(string directory, IEnumerable<string> mountInfo)
    {
        var path = Path.GetFullPath(directory).TrimEnd('/');
        if (path.Length == 0 || !Path.IsPathRooted(directory)) return false;
        var matches = new List<(string Path, string Type)>();
        foreach (var line in mountInfo)
        {
            var halves = line.Split(" - ", 2, StringSplitOptions.None);
            if (halves.Length != 2) continue;
            var fields = halves[0].Split(' ');
            if (fields.Length < 5) continue;
            var mount = fields[4].Replace("\\040", " ").Replace("\\011", "\t").Replace("\\134", "\\").TrimEnd('/');
            if (path == mount || path.StartsWith(mount + '/', StringComparison.Ordinal))
                matches.Add((mount, halves[1].Split(' ')[0]));
        }
        return matches.OrderByDescending(x => x.Path.Length).FirstOrDefault().Type is "nfs" or "nfs4" or "cifs" or "smb2";
    }
}

internal sealed record ArchiveTable(string Name, string SelectSql);
internal sealed record ArchiveFile(long Count, string Sha256);
internal sealed record ArchiveManifest(string Patch, int FrozenMatches, Dictionary<string, ArchiveFile> Rows);

/// <summary>Derive the raw-match cascade from EF metadata, including newly added child tables.</summary>
internal static class MatchArchiveCatalog
{
    internal static string Quote(string value) => '"' + value.Replace("\"", "\"\"") + '"';

    internal static IReadOnlyList<ArchiveTable> Build(IModel model, Guid chunkId)
    {
        var root = model.FindEntityType(typeof(Match)) ?? throw new InvalidOperationException("Match model missing");
        var result = new List<ArchiveTable>();
        var seen = new HashSet<IEntityType>();
        void Visit(IEntityType entity, string joins, int depth)
        {
            if (!seen.Add(entity)) throw new InvalidOperationException("Ambiguous/cyclic match cascade; review archive coverage");
            var table = entity.GetTableName() ?? throw new InvalidOperationException("Archive entity has no table");
            var alias = "t" + depth;
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var schema = entity.GetSchema() ?? "public";
            result.Add(new ArchiveTable(table, $"SELECT {alias}.* FROM {Quote(schema)}.{Quote(table)} {alias} {joins}"));
            foreach (var fk in entity.GetReferencingForeignKeys().Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade))
            {
                var child = fk.DeclaringEntityType;
                var childTable = StoreObjectIdentifier.Table(child.GetTableName()!, child.GetSchema());
                var childAlias = "t" + (depth + 1);
                var conditions = fk.Properties.Select((property, i) =>
                    $"{childAlias}.{Quote(property.GetColumnName(childTable)!)}={alias}.{Quote(fk.PrincipalKey.Properties[i].GetColumnName(store)!)}");
                var parentJoin = $"JOIN {Quote(schema)}.{Quote(table)} {alias} ON {string.Join(" AND ", conditions)} {joins}";
                Visit(child, parentJoin, depth + 1);
            }
        }
        var rootTable = StoreObjectIdentifier.Table(root.GetTableName()!, root.GetSchema());
        var rootKey = root.FindPrimaryKey()!.Properties.Single().GetColumnName(rootTable)!;
        Visit(root, $"JOIN \"MatchArchiveEntries\" a ON a.\"MatchId\"=t0.{Quote(rootKey)} AND a.\"ChunkId\"='{chunkId:D}'::uuid", 0);
        return result;
    }
}

/// <summary>COPY CSV can contain quoted newlines; physical line counts are not row counts.</summary>
internal sealed class CsvRecordCounter
{
    private bool quoted;
    private long records;
    public void Add(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (c == '"') quoted = !quoted;
            else if (c == '\n' && !quoted) records++;
        }
    }
    public long DataRows => !quoted && records > 0 ? records - 1 : throw new InvalidDataException("Incomplete archive CSV");
}

internal static class MatchArchiveFiles
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Resolve(string root, string relative)
    {
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(basePath, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Archive path escapes its configured root");
        var current = new DirectoryInfo(path);
        while (current is not null && current.FullName.StartsWith(basePath, StringComparison.Ordinal))
        {
            if (current.LinkTarget is not null) throw new InvalidDataException("Archive paths cannot contain symbolic links");
            current = current.Parent;
        }
        return path;
    }

    public static async Task<ArchiveFile> ExportAsync(TextReader source, string path, CancellationToken ct)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        var counter = new CsvRecordCounter();
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using (var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true))
                await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false), leaveOpen: true))
                {
                    var buffer = new char[64 * 1024];
                    int read;
                    while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
                    {
                        counter.Add(buffer.AsSpan(0, read));
                        await writer.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }
                file.Flush(flushToDisk: true);
            }
            var rows = counter.DataRows;
            var result = new ArchiveFile(rows, await ChecksumAsync(temporary, ct));
            await VerifyAsync(temporary, result, ct);
            File.Move(temporary, path, overwrite: true);
            return result;
        }
        finally { File.Delete(temporary); }
    }

    public static async Task VerifyAsync(string path, ArchiveFile expected, CancellationToken ct)
    {
        if (new FileInfo(path).LinkTarget is not null || await ChecksumAsync(path, ct) != expected.Sha256)
            throw new InvalidDataException("Archive checksum mismatch; source rows retained");
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var counter = new CsvRecordCounter();
        var buffer = new char[64 * 1024];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0) counter.Add(buffer.AsSpan(0, read));
        if (counter.DataRows != expected.Count) throw new InvalidDataException("Archive row verification failed; source rows retained");
    }

    private static async Task<string> ChecksumAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
    }

    public static async Task WriteAtomicAsync(string path, string text, CancellationToken ct)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }
}
