using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.Operations;

internal static class MatchRawRetentionPolicy
{
    public static async Task<List<string>> ProtectedPatchesAsync(TranscendenceContext db, int keep, CancellationToken ct)
    {
        var latest = await db.Patches.AsNoTracking().OrderByDescending(x => x.ReleaseDate)
            .Take(Math.Clamp(keep, 1, 12)).Select(x => x.Version).ToListAsync(ct);
        var active = await db.Patches.AsNoTracking().Where(x => x.IsActive).Select(x => x.Version).ToListAsync(ct);
        if (active.Count != 1) throw new InvalidOperationException("Raw match retention requires exactly one active patch");
        return latest.Concat(active).Distinct().ToList();
    }

    public static async Task<bool> ApplyAsync(TranscendenceContext db, Match match, MatchArchiveOptions options, CancellationToken ct)
    {
        if (!options.Enabled) return true;
        var retained = await ProtectedPatchesAsync(db, options.KeepPatches, ct);
        if (retained.Contains(match.Patch!) || !await db.Patches.AnyAsync(x => x.Version == match.Patch, ct)) return true;
        // Keep a tiny identity/status tombstone so a later refresh does not repeatedly fetch and
        // reinsert archived raw detail. Selected-player full history has its own durable facts flow.
        match.Status = FetchStatus.OutsideRetentionWindow;
        match.LastErrorMessage = "Raw match details are outside the configured active-host patch retention.";
        match.LastAttemptAt = DateTime.UtcNow;
        return false;
    }
}
