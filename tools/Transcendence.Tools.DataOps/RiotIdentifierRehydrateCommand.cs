using Camille.Enums;
using Camille.RiotGames;
using Npgsql;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.Tft.Account;

namespace Transcendence.Tools.DataOps;

internal static class RiotIdentifierRehydrateCommand
{
    public static async Task<int> RunAsync(CommandArguments args)
    {
        var options = RehydrateOptions.FromArgs(args);
        options.Validate();

        await using var connection = new NpgsqlConnection(options.TargetConnectionString);
        await connection.OpenAsync();

        var updatedLol = 0;
        var updatedTft = 0;

        if (options.IncludeLol)
            updatedLol = await RehydrateLolAsync(connection, options);

        if (options.IncludeTft)
            updatedTft = await RehydrateTftAsync(connection, options);

        Console.WriteLine($"Rehydration complete. LoL updated={updatedLol}, TFT updated={updatedTft}");
        return 0;
    }

    private static async Task<int> RehydrateLolAsync(NpgsqlConnection connection, RehydrateOptions options)
    {
        var api = RiotGamesApi.NewInstance(options.LolApiKey!);
        var rows = await LoadRowsAsync(connection, SchemaTables.For<Summoner>(), options);
        var updated = 0;

        foreach (var row in rows)
        {
            if (!PlatformRouteParser.TryParse(row.PlatformRegion!, out var platform))
                continue;

            var account = await api.AccountV1().GetByPuuidAsync(platform.ToRegional(), row.Puuid!, CancellationToken.None);
            var summoner = await api.SummonerV4().GetByPUUIDAsync(platform, row.Puuid!, CancellationToken.None);
            if (account == null || summoner == null)
                continue;

            await using var command = new NpgsqlCommand($"""
                UPDATE "{SchemaTables.For<Summoner>()}"
                SET "RiotSummonerId" = @riotSummonerId,
                    "AccountId" = @accountId,
                    "GameName" = @gameName,
                    "TagLine" = @tagLine,
                    "GameNameNormalized" = @gameNameNormalized,
                    "TagLineNormalized" = @tagLineNormalized,
                    "ProfileIconId" = @profileIconId,
                    "SummonerLevel" = @summonerLevel,
                    "RevisionDate" = @revisionDate,
                    "UpdatedAt" = @updatedAt
                WHERE "Id" = @id;
                """, connection);

            command.Parameters.AddWithValue("id", row.Id);
            command.Parameters.AddWithValue("riotSummonerId", (object?)summoner.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("accountId", (object?)summoner.AccountId ?? DBNull.Value);
            command.Parameters.AddWithValue("gameName", (object?)account.GameName?.Trim() ?? DBNull.Value);
            command.Parameters.AddWithValue("tagLine", (object?)account.TagLine?.Trim() ?? DBNull.Value);
            command.Parameters.AddWithValue("gameNameNormalized",
                (object?)NormalizeForLookup(account.GameName) ?? DBNull.Value);
            command.Parameters.AddWithValue("tagLineNormalized",
                (object?)NormalizeForLookup(account.TagLine) ?? DBNull.Value);
            command.Parameters.AddWithValue("profileIconId", summoner.ProfileIconId);
            command.Parameters.AddWithValue("summonerLevel", summoner.SummonerLevel);
            command.Parameters.AddWithValue("revisionDate", summoner.RevisionDate);
            command.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);

            updated += await command.ExecuteNonQueryAsync();
            Console.WriteLine($"  LoL rehydrated {row.Puuid}");

            if (options.DelayMilliseconds > 0)
                await Task.Delay(options.DelayMilliseconds);
        }

        return updated;
    }

    private static async Task<int> RehydrateTftAsync(NpgsqlConnection connection, RehydrateOptions options)
    {
        var api = RiotGamesApi.NewInstance(options.TftApiKey!);
        var rows = await LoadRowsAsync(connection, SchemaTables.For<TftSummoner>(), options);
        var updated = 0;

        foreach (var row in rows)
        {
            if (!PlatformRouteParser.TryParse(row.PlatformRegion!, out var platform))
                continue;

            var account = await api.AccountV1().GetByPuuidAsync(platform.ToRegional(), row.Puuid!, CancellationToken.None);
            var summoner = await api.TftSummonerV1().GetByPUUIDAsync(platform, row.Puuid!, CancellationToken.None);
            if (account == null || summoner == null)
                continue;

            await using var command = new NpgsqlCommand($"""
                UPDATE "{SchemaTables.For<TftSummoner>()}"
                SET "RiotSummonerId" = @riotSummonerId,
                    "AccountId" = @accountId,
                    "GameName" = @gameName,
                    "TagLine" = @tagLine,
                    "GameNameNormalized" = @gameNameNormalized,
                    "TagLineNormalized" = @tagLineNormalized,
                    "ProfileIconId" = @profileIconId,
                    "SummonerLevel" = @summonerLevel,
                    "RevisionDate" = @revisionDate,
                    "UpdatedAt" = @updatedAt
                WHERE "Id" = @id;
                """, connection);

            command.Parameters.AddWithValue("id", row.Id);
            command.Parameters.AddWithValue("riotSummonerId", (object?)summoner.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("accountId", (object?)summoner.AccountId ?? DBNull.Value);
            command.Parameters.AddWithValue("gameName", (object?)account.GameName?.Trim() ?? DBNull.Value);
            command.Parameters.AddWithValue("tagLine", (object?)account.TagLine?.Trim() ?? DBNull.Value);
            command.Parameters.AddWithValue("gameNameNormalized",
                (object?)NormalizeForLookup(account.GameName) ?? DBNull.Value);
            command.Parameters.AddWithValue("tagLineNormalized",
                (object?)NormalizeForLookup(account.TagLine) ?? DBNull.Value);
            command.Parameters.AddWithValue("profileIconId", summoner.ProfileIconId);
            command.Parameters.AddWithValue("summonerLevel", summoner.SummonerLevel);
            command.Parameters.AddWithValue("revisionDate", summoner.RevisionDate);
            command.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);

            updated += await command.ExecuteNonQueryAsync();
            Console.WriteLine($"  TFT rehydrated {row.Puuid}");

            if (options.DelayMilliseconds > 0)
                await Task.Delay(options.DelayMilliseconds);
        }

        return updated;
    }

    private static async Task<List<RehydrateRow>> LoadRowsAsync(
        NpgsqlConnection connection,
        string tableName,
        RehydrateOptions options)
    {
        var rows = new List<RehydrateRow>();
        var sql = $"""
                   SELECT "Id", "Puuid", "PlatformRegion"
                   FROM "{tableName}"
                   WHERE "Puuid" IS NOT NULL
                     AND "Puuid" <> ''
                     {(options.OnlyMissing ? "AND (\"RiotSummonerId\" IS NULL OR \"RiotSummonerId\" = '' OR \"AccountId\" IS NULL OR \"AccountId\" = '' OR \"GameName\" IS NULL OR \"TagLine\" IS NULL)" : string.Empty)}
                   ORDER BY "UpdatedAt" DESC
                   LIMIT @limit;
                   """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", options.Limit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new RehydrateRow(
                reader.GetGuid(reader.GetOrdinal("Id")),
                reader["Puuid"] as string,
                reader["PlatformRegion"] as string));
        }

        return rows;
    }

    private static string? NormalizeForLookup(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private sealed record RehydrateRow(Guid Id, string? Puuid, string? PlatformRegion);

    private sealed record RehydrateOptions(
        string TargetConnectionString,
        bool IncludeLol,
        bool IncludeTft,
        int Limit,
        int DelayMilliseconds,
        bool OnlyMissing,
        string? LolApiKey,
        string? TftApiKey)
    {
        public static RehydrateOptions FromArgs(CommandArguments args)
        {
            var games = (args.GetString("games", "all") ?? "all").Trim().ToLowerInvariant();
            var includeLol = games is "all" or "lol";
            var includeTft = games is "all" or "tft";

            return new RehydrateOptions(
                args.GetString("target")
                ?? Environment.GetEnvironmentVariable("TRN_TARGET_DB")
                ?? "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=changme",
                includeLol,
                includeTft,
                Math.Max(1, args.GetInt("limit", 100)),
                Math.Max(0, args.GetInt("delay-ms", 75)),
                args.HasFlag("only-missing"),
                Environment.GetEnvironmentVariable("TRN_RIOT_API_KEY_LOL")
                ?? Environment.GetEnvironmentVariable("RIOT_API_KEY_LOL"),
                Environment.GetEnvironmentVariable("TRN_RIOT_API_KEY_TFT")
                ?? Environment.GetEnvironmentVariable("RIOT_API_KEY_TFT"));
        }

        public void Validate()
        {
            if (!IncludeLol && !IncludeTft)
                throw new InvalidOperationException("--games must be one of all, lol, or tft.");

            if (IncludeLol && string.IsNullOrWhiteSpace(LolApiKey))
                throw new InvalidOperationException("LoL rehydration requires TRN_RIOT_API_KEY_LOL or RIOT_API_KEY_LOL.");

            if (IncludeTft && string.IsNullOrWhiteSpace(TftApiKey))
                throw new InvalidOperationException("TFT rehydration requires TRN_RIOT_API_KEY_TFT or RIOT_API_KEY_TFT.");
        }
    }
}
