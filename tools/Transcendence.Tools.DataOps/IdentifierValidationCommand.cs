using Camille.Enums;
using Camille.RiotGames;
using Npgsql;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.Tft.Account;

namespace Transcendence.Tools.DataOps;

internal static class IdentifierValidationCommand
{
    public static async Task<int> RunAsync(CommandArguments args)
    {
        var connectionString = args.GetString("db")
            ?? Environment.GetEnvironmentVariable("TRN_TARGET_DB")
            ?? "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=changme";
        var sampleSize = Math.Max(1, args.GetInt("sample-size", 5));
        var skipLive = args.HasFlag("skip-live");

        Console.WriteLine("Identifier policy");
        foreach (var line in BuildUsagePolicyLines())
            Console.WriteLine($"  {line}");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        Console.WriteLine();
        Console.WriteLine("Database stats");
        await PrintDatabaseStatsAsync(connection);

        if (skipLive)
        {
            Console.WriteLine();
            Console.WriteLine("Live Riot validation skipped.");
            return 0;
        }

        var lolKey = ResolveRiotKey("TRN_RIOT_API_KEY_LOL", "RIOT_API_KEY_LOL");
        var tftKey = ResolveRiotKey("TRN_RIOT_API_KEY_TFT", "RIOT_API_KEY_TFT");

        if (string.IsNullOrWhiteSpace(lolKey) && string.IsNullOrWhiteSpace(tftKey))
        {
            Console.WriteLine();
            Console.WriteLine("Live Riot validation skipped because no Riot API keys were provided.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Live Riot validation");

        var warnings = 0;
        if (!string.IsNullOrWhiteSpace(lolKey))
            warnings += await ValidateLolAsync(connection, lolKey!, sampleSize);
        if (!string.IsNullOrWhiteSpace(tftKey))
            warnings += await ValidateTftAsync(connection, tftKey!, sampleSize);

        return warnings == 0 ? 0 : 1;
    }

    private static IReadOnlyList<string> BuildUsagePolicyLines()
    {
        return
        [
            "Canonical durable Riot identity: Summoners.Puuid and TftSummoners.Puuid.",
            "Canonical user lookup: normalized Riot ID (gameName + tagLine + platform region).",
            "LoL autosuggest/search: SummonersController -> ISummonerRepository.SearchByPrefixAsync -> stored Riot ID fields + match participation gate.",
            "LoL profile and stats: SummonersController -> FindByRiotIdAsync -> local summoner Guid for downstream stats and matches.",
            "TFT search/profile: TftSummonersController + ITftSummonerReadService -> FindByRiotIdAsync -> local summoner Guid for matches.",
            "Favorites, pro roster, live-game snapshots, and participant joins key on PUUID or local GUIDs, not Riot encrypted IDs.",
            "RiotSummonerId and AccountId are non-canonical refresh artifacts and should be safe to null or rehydrate from PUUID."
        ];
    }

    private static async Task PrintDatabaseStatsAsync(NpgsqlConnection connection)
    {
        var stats = new List<(string Name, string Query)>
        {
            ("LoL summoners", $"""
                SELECT CONCAT(
                    'total=', COUNT(*),
                    ', puuid_missing=', COUNT(*) FILTER (WHERE "Puuid" IS NULL OR "Puuid" = ''),
                    ', riot_summoner_id_missing=', COUNT(*) FILTER (WHERE "RiotSummonerId" IS NULL OR "RiotSummonerId" = ''),
                    ', account_id_missing=', COUNT(*) FILTER (WHERE "AccountId" IS NULL OR "AccountId" = ''),
                    ', lookup_ready=', COUNT(*) FILTER (WHERE "GameNameNormalized" IS NOT NULL AND "TagLineNormalized" IS NOT NULL)
                )
                FROM "{SchemaTables.For<Summoner>()}";
                """),
            ("TFT summoners", $"""
                SELECT CONCAT(
                    'total=', COUNT(*),
                    ', puuid_missing=', COUNT(*) FILTER (WHERE "Puuid" IS NULL OR "Puuid" = ''),
                    ', riot_summoner_id_missing=', COUNT(*) FILTER (WHERE "RiotSummonerId" IS NULL OR "RiotSummonerId" = ''),
                    ', account_id_missing=', COUNT(*) FILTER (WHERE "AccountId" IS NULL OR "AccountId" = ''),
                    ', lookup_ready=', COUNT(*) FILTER (WHERE "GameNameNormalized" IS NOT NULL AND "TagLineNormalized" IS NOT NULL)
                )
                FROM "{SchemaTables.For<TftSummoner>()}";
                """),
            ("LoL favorites keyed by PUUID", $"""
                SELECT CONCAT('favorites=', COUNT(*), ', distinct_puuids=', COUNT(DISTINCT "SummonerPuuid"))
                FROM "{SchemaTables.For<UserFavoriteSummoner>()}";
                """),
            ("LoL live snapshots keyed by PUUID", $"""
                SELECT CONCAT('snapshots=', COUNT(*), ', distinct_puuids=', COUNT(DISTINCT "Puuid"))
                FROM "{SchemaTables.For<LiveGameSnapshot>()}";
                """),
            ("LoL pro roster keyed by PUUID", $"""
                SELECT CONCAT('tracked=', COUNT(*), ', distinct_puuids=', COUNT(DISTINCT "Puuid"))
                FROM "{SchemaTables.For<TrackedProSummoner>()}";
                """)
        };

        foreach (var stat in stats)
        {
            await using var command = new NpgsqlCommand(stat.Query, connection);
            var value = (string?)await command.ExecuteScalarAsync() ?? "n/a";
            Console.WriteLine($"  {stat.Name}: {value}");
        }
    }

    private static async Task<int> ValidateLolAsync(NpgsqlConnection connection, string apiKey, int sampleSize)
    {
        var warnings = 0;
        var api = RiotGamesApi.NewInstance(apiKey);
        var rows = await LoadRowsAsync(connection, $"""
            SELECT "Puuid", "PlatformRegion", "GameName", "TagLine", "RiotSummonerId", "AccountId"
            FROM "{SchemaTables.For<Summoner>()}"
            WHERE "Puuid" IS NOT NULL AND "Puuid" <> '' AND "PlatformRegion" IS NOT NULL
            ORDER BY "UpdatedAt" DESC
            LIMIT @sampleSize;
            """, sampleSize);

        foreach (var row in rows)
        {
            if (!PlatformRouteParser.TryParse(row.PlatformRegion!, out var platform))
            {
                Console.WriteLine($"  LoL {row.Puuid}: skipped invalid platform region '{row.PlatformRegion}'.");
                continue;
            }

            var account = await api.AccountV1().GetByPuuidAsync(platform.ToRegional(), row.Puuid!, CancellationToken.None);
            var summoner = await api.SummonerV4().GetByPUUIDAsync(platform, row.Puuid!, CancellationToken.None);

            var riotIdStable = string.Equals(account?.Puuid, row.Puuid, StringComparison.Ordinal)
                               && string.Equals(account?.GameName, row.GameName, StringComparison.Ordinal)
                               && string.Equals(account?.TagLine, row.TagLine, StringComparison.Ordinal);
            var encryptedIdsMatch = string.Equals(summoner?.Id, row.RiotSummonerId, StringComparison.Ordinal)
                                    && string.Equals(summoner?.AccountId, row.AccountId, StringComparison.Ordinal);

            Console.WriteLine(
                $"  LoL {row.Puuid}: canonical={(riotIdStable ? "ok" : "mismatch")}, encrypted_ids={(encryptedIdsMatch ? "same" : "drifted")}");

            if (!riotIdStable)
                warnings++;
        }

        return warnings;
    }

    private static async Task<int> ValidateTftAsync(NpgsqlConnection connection, string apiKey, int sampleSize)
    {
        var warnings = 0;
        var api = RiotGamesApi.NewInstance(apiKey);
        var rows = await LoadRowsAsync(connection, $"""
            SELECT "Puuid", "PlatformRegion", "GameName", "TagLine", "RiotSummonerId", "AccountId"
            FROM "{SchemaTables.For<TftSummoner>()}"
            WHERE "Puuid" IS NOT NULL AND "Puuid" <> '' AND "PlatformRegion" IS NOT NULL
            ORDER BY "UpdatedAt" DESC
            LIMIT @sampleSize;
            """, sampleSize);

        foreach (var row in rows)
        {
            if (!PlatformRouteParser.TryParse(row.PlatformRegion!, out var platform))
            {
                Console.WriteLine($"  TFT {row.Puuid}: skipped invalid platform region '{row.PlatformRegion}'.");
                continue;
            }

            var account = await api.AccountV1().GetByPuuidAsync(platform.ToRegional(), row.Puuid!, CancellationToken.None);
            var summoner = await api.TftSummonerV1().GetByPUUIDAsync(platform, row.Puuid!, CancellationToken.None);

            var riotIdStable = string.Equals(account?.Puuid, row.Puuid, StringComparison.Ordinal)
                               && string.Equals(account?.GameName, row.GameName, StringComparison.Ordinal)
                               && string.Equals(account?.TagLine, row.TagLine, StringComparison.Ordinal);
            var encryptedIdsMatch = string.Equals(summoner?.Id, row.RiotSummonerId, StringComparison.Ordinal)
                                    && string.Equals(summoner?.AccountId, row.AccountId, StringComparison.Ordinal);

            Console.WriteLine(
                $"  TFT {row.Puuid}: canonical={(riotIdStable ? "ok" : "mismatch")}, encrypted_ids={(encryptedIdsMatch ? "same" : "drifted")}");

            if (!riotIdStable)
                warnings++;
        }

        return warnings;
    }

    private static async Task<List<IdentifierRow>> LoadRowsAsync(NpgsqlConnection connection, string sql, int sampleSize)
    {
        var rows = new List<IdentifierRow>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("sampleSize", sampleSize);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new IdentifierRow(
                reader["Puuid"] as string,
                reader["PlatformRegion"] as string,
                reader["GameName"] as string,
                reader["TagLine"] as string,
                reader["RiotSummonerId"] as string,
                reader["AccountId"] as string));
        }

        return rows;
    }

    private static string? ResolveRiotKey(string preferred, string fallback)
    {
        return Environment.GetEnvironmentVariable(preferred)
            ?? Environment.GetEnvironmentVariable(fallback);
    }

    private sealed record IdentifierRow(
        string? Puuid,
        string? PlatformRegion,
        string? GameName,
        string? TagLine,
        string? RiotSummonerId,
        string? AccountId);
}
