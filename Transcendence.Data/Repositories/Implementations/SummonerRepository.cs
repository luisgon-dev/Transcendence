using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class SummonerRepository(TranscendenceContext context, IRankRepository rankRepository) : ISummonerRepository
{
    public async Task<IReadOnlyList<SummonerSearchCandidate>> SearchByPrefixAsync(
        string platformRegion,
        string gameNamePrefix,
        string? tagLinePrefix,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlatformRegion = string.IsNullOrWhiteSpace(platformRegion) ? null : platformRegion.Trim();
        var normalizedGameNamePrefix = NormalizeForLookup(gameNamePrefix);
        var normalizedTagLinePrefix = NormalizeForLookup(tagLinePrefix);
        var safeLimit = Math.Clamp(limit, 1, 25);

        if (normalizedPlatformRegion == null || normalizedGameNamePrefix == null)
            return [];

        var gameNameLike = $"{normalizedGameNamePrefix}%";
        var tagLineLike = normalizedTagLinePrefix == null ? null : $"{normalizedTagLinePrefix}%";

        var query = context.Summoners
            .AsNoTracking()
            .Where(s =>
                s.PlatformRegion == normalizedPlatformRegion &&
                s.GameName != null &&
                s.TagLine != null &&
                s.GameNameNormalized != null &&
                s.TagLineNormalized != null &&
                context.MatchParticipants.Any(mp => mp.SummonerId == s.Id) &&
                EF.Functions.Like(s.GameNameNormalized, gameNameLike));

        if (tagLineLike != null)
        {
            query = query.Where(s => EF.Functions.Like(s.TagLineNormalized!, tagLineLike));
        }

        return await query
            .OrderBy(s => s.GameNameNormalized == normalizedGameNamePrefix ? 0 : 1)
            .ThenBy(s =>
                normalizedTagLinePrefix != null && s.TagLineNormalized == normalizedTagLinePrefix ? 0 : 1)
            .ThenBy(s => s.GameName)
            .ThenBy(s => s.TagLine)
            .Take(safeLimit)
            .Select(s => new SummonerSearchCandidate(
                s.PlatformRegion!,
                s.GameName!,
                s.TagLine!,
                s.ProfileIconId))
            .ToListAsync(cancellationToken);
    }

    public async Task<Summoner?> GetSummonerByPuuidAsync(string puuid,
        Func<IQueryable<Summoner>, IQueryable<Summoner>>? includes = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Summoner> query = context.Summoners;

        if (includes != null) query = includes(query);

        return await query.FirstOrDefaultAsync(x => x.Puuid == puuid, cancellationToken);
    }

    public async Task<Summoner?> FindByRiotIdAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        Func<IQueryable<Summoner>, IQueryable<Summoner>>? includes = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Summoner> query = context.Summoners;
        if (includes != null)
        {
            query = includes(query).AsSplitQuery();
        }

        var normalizedPlatformRegion = string.IsNullOrWhiteSpace(platformRegion) ? null : platformRegion.Trim();
        var normalizedGameName = NormalizeValue(gameName);
        var normalizedTagLine = NormalizeValue(tagLine);
        var normalizedGameNameKey = NormalizeForLookup(normalizedGameName);
        var normalizedTagLineKey = NormalizeForLookup(normalizedTagLine);

        if (normalizedPlatformRegion == null ||
            normalizedGameName == null ||
            normalizedTagLine == null ||
            normalizedGameNameKey == null ||
            normalizedTagLineKey == null)
            return null;

        query = query
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.Id);

        // Fast path: normalized match can leverage the composite index.
        var normalizedMatch = await query.FirstOrDefaultAsync(x =>
                x.PlatformRegion == normalizedPlatformRegion &&
                x.GameNameNormalized == normalizedGameNameKey &&
                x.TagLineNormalized == normalizedTagLineKey,
            cancellationToken);

        if (normalizedMatch != null)
            return normalizedMatch;

        // Secondary exact match for rows written before normalization fields were introduced.
        var exactMatch = await query.FirstOrDefaultAsync(x =>
                x.PlatformRegion == normalizedPlatformRegion &&
                x.GameName == normalizedGameName &&
                x.TagLine == normalizedTagLine,
            cancellationToken);

        if (exactMatch != null)
            return exactMatch;

        // Legacy fallback for older rows where normalized fields are not present.
        return await query.FirstOrDefaultAsync(x =>
                x.PlatformRegion == normalizedPlatformRegion &&
                x.GameNameNormalized == null &&
                x.TagLineNormalized == null &&
                x.GameName != null &&
                x.TagLine != null &&
                x.GameName.ToUpper() == normalizedGameNameKey &&
                x.TagLine.ToUpper() == normalizedTagLineKey,
            cancellationToken);
    }

    public async Task<Summoner> AddOrUpdateSummonerAsync(Summoner summoner, CancellationToken cancellationToken)
    {
        summoner.GameName = NormalizeValue(summoner.GameName);
        summoner.TagLine = NormalizeValue(summoner.TagLine);
        summoner.GameNameNormalized = NormalizeForLookup(summoner.GameName);
        summoner.TagLineNormalized = NormalizeForLookup(summoner.TagLine);
        summoner.UpdatedAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(summoner.Puuid))
            throw new InvalidOperationException("Summoner PUUID is required for upsert.");

        var persistedId = await UpsertSummonerAsync(summoner, cancellationToken);
        var persistedSummoner = await context.Summoners
            .Include(s => s.Ranks)
            .SingleAsync(s => s.Id == persistedId, cancellationToken);

        if (summoner.Ranks.Count > 0)
        {
            await rankRepository.AddOrUpdateRank(persistedSummoner, summoner.Ranks.ToList(), cancellationToken);
        }

        return persistedSummoner;
    }

    public async Task<IReadOnlyList<Summoner>> FindByRiotIdsAsync(
        string platformRegion,
        IReadOnlyList<(string GameName, string TagLine)> riotIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlatformRegion = string.IsNullOrWhiteSpace(platformRegion) ? null : platformRegion.Trim();
        if (normalizedPlatformRegion == null || riotIds.Count == 0)
            return [];

        // Normalize all lookup keys upfront.
        var lookupKeys = riotIds
            .Select(r => (GameName: NormalizeForLookup(r.GameName), TagLine: NormalizeForLookup(r.TagLine)))
            .Where(r => r.GameName != null && r.TagLine != null)
            .ToList();

        if (lookupKeys.Count == 0)
            return [];

        // Extract distinct normalized values for the WHERE IN clause.
        var gameNames = lookupKeys.Select(k => k.GameName!).Distinct(StringComparer.Ordinal).ToList();
        var tagLines = lookupKeys.Select(k => k.TagLine!).Distinct(StringComparer.Ordinal).ToList();

        // Fetch candidates matching region + any of the game names + any of the tag lines.
        // This is a superset; we filter to exact pairs in memory.
        var candidates = await context.Summoners
            .AsNoTracking()
            .Include(s => s.Ranks)
            .Where(s =>
                s.PlatformRegion == normalizedPlatformRegion &&
                s.GameNameNormalized != null &&
                s.TagLineNormalized != null &&
                gameNames.Contains(s.GameNameNormalized) &&
                tagLines.Contains(s.TagLineNormalized))
            .ToListAsync(cancellationToken);

        // Filter to exact (gameName, tagLine) pairs.
        var lookupSet = lookupKeys
            .Select(k => (k.GameName!, k.TagLine!))
            .ToHashSet();

        return candidates
            .Where(s => lookupSet.Contains((s.GameNameNormalized!, s.TagLineNormalized!)))
            .GroupBy(s => (s.GameNameNormalized, s.TagLineNormalized))
            .Select(g => g.OrderByDescending(s => s.UpdatedAt).First())
            .ToList();
    }

    private async Task<Guid> UpsertSummonerAsync(Summoner summoner, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO "Summoners"
                           ("Id", "RiotSummonerId", "SummonerName", "ProfileIconId", "SummonerLevel", "RevisionDate",
                            "Puuid", "GameName", "TagLine", "GameNameNormalized", "TagLineNormalized",
                            "AccountId", "PlatformRegion", "Region", "UpdatedAt")
                           VALUES
                           (@id, @riotSummonerId, @summonerName, @profileIconId, @summonerLevel, @revisionDate,
                            @puuid, @gameName, @tagLine, @gameNameNormalized, @tagLineNormalized,
                            @accountId, @platformRegion, @region, @updatedAt)
                           ON CONFLICT ("Puuid")
                           DO UPDATE SET
                               "RiotSummonerId" = EXCLUDED."RiotSummonerId",
                               "SummonerName" = EXCLUDED."SummonerName",
                               "ProfileIconId" = EXCLUDED."ProfileIconId",
                               "SummonerLevel" = EXCLUDED."SummonerLevel",
                               "RevisionDate" = EXCLUDED."RevisionDate",
                               "GameName" = EXCLUDED."GameName",
                               "TagLine" = EXCLUDED."TagLine",
                               "GameNameNormalized" = EXCLUDED."GameNameNormalized",
                               "TagLineNormalized" = EXCLUDED."TagLineNormalized",
                               "AccountId" = EXCLUDED."AccountId",
                               "PlatformRegion" = EXCLUDED."PlatformRegion",
                               "Region" = EXCLUDED."Region",
                               "UpdatedAt" = EXCLUDED."UpdatedAt"
                           RETURNING "Id";
                           """;

        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;

        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = 120;

            var currentTransaction = context.Database.CurrentTransaction?.GetDbTransaction();
            if (currentTransaction != null)
                command.Transaction = currentTransaction;

            AddParameter(command, "@id", summoner.Id == Guid.Empty ? Guid.NewGuid() : summoner.Id);
            AddParameter(command, "@riotSummonerId", summoner.RiotSummonerId);
            AddParameter(command, "@summonerName", summoner.SummonerName);
            AddParameter(command, "@profileIconId", summoner.ProfileIconId);
            AddParameter(command, "@summonerLevel", summoner.SummonerLevel);
            AddParameter(command, "@revisionDate", summoner.RevisionDate);
            AddParameter(command, "@puuid", summoner.Puuid);
            AddParameter(command, "@gameName", summoner.GameName);
            AddParameter(command, "@tagLine", summoner.TagLine);
            AddParameter(command, "@gameNameNormalized", summoner.GameNameNormalized);
            AddParameter(command, "@tagLineNormalized", summoner.TagLineNormalized);
            AddParameter(command, "@accountId", summoner.AccountId);
            AddParameter(command, "@platformRegion", summoner.PlatformRegion);
            AddParameter(command, "@region", summoner.Region);
            AddParameter(command, "@updatedAt", summoner.UpdatedAt);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is Guid id)
                return id;

            if (result is string value && Guid.TryParse(value, out var parsedId))
                return parsedId;

            throw new InvalidOperationException("Summoner upsert did not return a valid Id.");
        }
        catch (DbException ex) when (ex.SqlState == UniqueViolationSqlState)
        {
            // ON CONFLICT ("Puuid") already absorbs PUUID collisions, so the only unique violation that
            // can escape here is IX_Summoners_SearchPrefix: the (PlatformRegion, GameName, TagLine) is held
            // by a row with a different PUUID. Riot riot-ids are mutable/reusable while the PUUID is the
            // stable identity, and match data carries the names as they were at match time — so a new-PUUID
            // upsert can collide on the riot-id index. Reuse the existing row instead of failing the whole
            // ingestion job. (If no such row resolves, this was an unexpected conflict — rethrow.)
            var currentTransaction = context.Database.CurrentTransaction?.GetDbTransaction();
            var existingId = await TryGetExistingSummonerIdByRiotIdAsync(connection, currentTransaction, summoner, cancellationToken);
            if (existingId is { } resolvedId)
                return resolvedId;
            throw;
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }

    private const string UniqueViolationSqlState = "23505";

    private static async Task<Guid?> TryGetExistingSummonerIdByRiotIdAsync(
        DbConnection connection,
        DbTransaction? transaction,
        Summoner summoner,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT "Id" FROM "Summoners"
                           WHERE "PlatformRegion" = @platformRegion
                             AND "GameNameNormalized" = @gameNameNormalized
                             AND "TagLineNormalized" = @tagLineNormalized
                           LIMIT 1;
                           """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        if (transaction != null)
            command.Transaction = transaction;

        AddParameter(command, "@platformRegion", summoner.PlatformRegion);
        AddParameter(command, "@gameNameNormalized", summoner.GameNameNormalized);
        AddParameter(command, "@tagLineNormalized", summoner.TagLineNormalized);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is Guid id)
            return id;
        if (result is string value && Guid.TryParse(value, out var parsedId))
            return parsedId;
        return null;
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeForLookup(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }
}
