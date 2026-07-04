using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Service.Core.Services.Analysis;

public sealed record RankedSeasonWindow(string SeasonKey, string DisplayName, DateTime StartUtc, DateTime? EndUtc);

public static class RankedSeasonResolver
{
    public static async Task<RankedSeasonWindow> GetActiveSeasonAsync(
        TranscendenceContext db,
        DateTime nowUtc,
        CancellationToken ct)
    {
        nowUtc = EnsureUtc(nowUtc);

        var configured = await db.RankedSeasons
            .AsNoTracking()
            .Where(s => s.StartUtc <= nowUtc && (s.EndUtc == null || s.EndUtc > nowUtc))
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.StartUtc)
            .Select(s => new RankedSeasonWindow(s.SeasonKey, s.DisplayName, s.StartUtc, s.EndUtc))
            .FirstOrDefaultAsync(ct);

        return configured ?? BuildCalendarSeason(nowUtc);
    }

    public static async Task<IReadOnlyList<RankedSeasonWindow>> GetConfiguredSeasonsAsync(
        TranscendenceContext db,
        CancellationToken ct)
    {
        return await db.RankedSeasons
            .AsNoTracking()
            .OrderBy(s => s.StartUtc)
            .Select(s => new RankedSeasonWindow(s.SeasonKey, s.DisplayName, s.StartUtc, s.EndUtc))
            .ToListAsync(ct);
    }

    public static string ResolveSeasonKey(DateTime matchUtc, IReadOnlyList<RankedSeasonWindow> configuredSeasons)
    {
        matchUtc = EnsureUtc(matchUtc);
        var configured = configuredSeasons
            .Where(s => s.StartUtc <= matchUtc && (s.EndUtc == null || s.EndUtc > matchUtc))
            .OrderByDescending(s => s.StartUtc)
            .FirstOrDefault();

        return configured?.SeasonKey ?? matchUtc.Year.ToString();
    }

    private static RankedSeasonWindow BuildCalendarSeason(DateTime utc)
    {
        var year = utc.Year;
        return new RankedSeasonWindow(
            year.ToString(),
            $"Season {year}",
            new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return value;

        if (value.Kind == DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return value.ToUniversalTime();
    }
}
