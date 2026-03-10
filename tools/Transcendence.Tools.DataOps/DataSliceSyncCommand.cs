using System.Data;
using Camille.Enums;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Models.Service;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Data.Models.Tft.Match;
using Transcendence.Data.Models.Tft.Static;
using Npgsql;

namespace Transcendence.Tools.DataOps;

internal static class DataSliceSyncCommand
{
    private static readonly string[] DefaultRegions = ["NA1", "EUW1", "KR"];
    private static readonly string[] SliceTables =
    [
        SchemaTables.For<MatchParticipantRune>(),
        SchemaTables.For<MatchParticipantItem>(),
        SchemaTables.For<MatchParticipantTimelineSnapshot>(),
        SchemaTables.For<MatchTimelineFetchState>(),
        SchemaTables.For<MatchParticipant>(),
        SchemaTables.For<MatchBan>(),
        SchemaTables.ForManyToMany<Match, Summoner>(),
        SchemaTables.For<Match>(),
        SchemaTables.For<LiveGameSnapshot>(),
        SchemaTables.For<SummonerIngestionCursor>(),
        SchemaTables.For<HistoricalRank>(),
        SchemaTables.For<Rank>(),
        SchemaTables.For<TrackedProSummoner>(),
        SchemaTables.For<Summoner>(),
        SchemaTables.For<CurrentChampionLoadout>(),
        SchemaTables.For<CurrentDataParameters>(),
        SchemaTables.For<RuneVersion>(),
        SchemaTables.For<ItemVersion>(),
        SchemaTables.For<Patch>(),
        SchemaTables.For<TftMatchParticipantUnit>(),
        SchemaTables.For<TftMatchParticipantTrait>(),
        SchemaTables.For<TftMatchParticipantAugment>(),
        SchemaTables.For<TftMatchParticipant>(),
        SchemaTables.For<TftMatch>(),
        SchemaTables.For<TftSummonerIngestionCursor>(),
        SchemaTables.For<TftHistoricalRank>(),
        SchemaTables.For<TftRank>(),
        SchemaTables.For<TftSummoner>(),
        SchemaTables.For<TftUnitVersion>(),
        SchemaTables.For<TftItemVersion>(),
        SchemaTables.For<TftTraitVersion>(),
        SchemaTables.For<TftAugmentVersion>(),
        SchemaTables.For<TftSet>(),
        SchemaTables.For<TftPatch>()
    ];

    public static async Task<int> RunAsync(CommandArguments args)
    {
        var options = SliceSyncOptions.FromArgs(args);
        options.Validate();

        await using var source = new NpgsqlConnection(options.SourceConnectionString);
        await using var target = new NpgsqlConnection(options.TargetConnectionString);
        await source.OpenAsync();
        await target.OpenAsync();

        await VerifySchemaCompatibilityAsync(source, target);

        await using var sourceTx = await source.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using var targetTx = await target.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        Console.WriteLine($"Preparing slice from regions: {string.Join(", ", options.Regions)}");
        await PrepareSourceSliceAsync(source, sourceTx, options);

        await ExecuteNonQueryAsync(
            target,
            targetTx,
            $"TRUNCATE TABLE {string.Join(", ", SliceTables.Select(QuoteIdentifier))} RESTART IDENTITY CASCADE;");

        var copyPlan = BuildCopyPlan(options);
        var results = new List<(string Table, long Rows)>();

        foreach (var definition in copyPlan)
        {
            Console.WriteLine($"Copying {definition.TableName}...");
            var rows = await CopyTableAsync(source, sourceTx, target, targetTx, definition.TableName, definition.SourceQuery);
            results.Add((definition.TableName, rows));
        }

        await targetTx.CommitAsync();
        await sourceTx.CommitAsync();

        Console.WriteLine("Slice import completed.");
        foreach (var result in results)
            Console.WriteLine($"  {result.Table}: {result.Rows} row(s)");

        return 0;
    }

    private static async Task VerifySchemaCompatibilityAsync(NpgsqlConnection source, NpgsqlConnection target)
    {
        const string sql = """
                           SELECT "MigrationId"
                           FROM "__EFMigrationsHistory"
                           ORDER BY "MigrationId" DESC
                           LIMIT 1;
                           """;

        var sourceMigration = await ExecuteScalarAsync<string>(source, transaction: null, sql);
        var targetMigration = await ExecuteScalarAsync<string>(target, transaction: null, sql);

        if (string.IsNullOrWhiteSpace(sourceMigration) || string.IsNullOrWhiteSpace(targetMigration))
            throw new InvalidOperationException("Unable to read __EFMigrationsHistory from source or target database.");

        if (!string.Equals(sourceMigration, targetMigration, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Schema mismatch detected. Source migration={sourceMigration}, target migration={targetMigration}.");
        }
    }

    private static async Task PrepareSourceSliceAsync(
        NpgsqlConnection source,
        NpgsqlTransaction transaction,
        SliceSyncOptions options)
    {
        var patches = QuoteIdentifier(SchemaTables.For<Patch>());
        var matches = QuoteIdentifier(SchemaTables.For<Match>());
        var matchParticipants = QuoteIdentifier(SchemaTables.For<MatchParticipant>());
        var tftPatches = QuoteIdentifier(SchemaTables.For<TftPatch>());
        var tftMatches = QuoteIdentifier(SchemaTables.For<TftMatch>());
        var tftParticipants = QuoteIdentifier(SchemaTables.For<TftMatchParticipant>());

        await ExecuteNonQueryAsync(source, transaction, """
            CREATE TEMP TABLE slice_regions ("Region" text PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE slice_lol_patch_versions ("Version" text PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE slice_tft_patch_versions ("Version" text PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE slice_lol_match_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE slice_lol_summoner_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE slice_lol_participant_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE slice_tft_match_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE slice_tft_summoner_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE slice_tft_participant_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            """);

        await ExecuteNonQueryAsync(
            source,
            transaction,
            """INSERT INTO slice_regions ("Region") SELECT UNNEST(@regions);""",
            new NpgsqlParameter("regions", options.Regions));

        if (!options.SkipLol)
        {
            await ExecuteNonQueryAsync(
                source,
                transaction,
                $"""
                INSERT INTO slice_lol_patch_versions ("Version")
                SELECT "Version"
                FROM {patches}
                ORDER BY CASE WHEN "IsActive" THEN 0 ELSE 1 END, "ReleaseDate" DESC
                LIMIT @patchDepth;
                """,
                new NpgsqlParameter("patchDepth", options.PatchDepth));

            await ExecuteNonQueryAsync(
                source,
                transaction,
                $"""
                WITH candidates AS (
                    SELECT
                        m."Id",
                        m."PlatformRegion",
                        ROW_NUMBER() OVER (
                            PARTITION BY m."PlatformRegion"
                            ORDER BY m."MatchDate" DESC, m."Id") AS row_number,
                        COUNT(*) OVER (PARTITION BY m."PlatformRegion") AS total_in_region
                    FROM {matches} m
                    JOIN slice_regions r ON r."Region" = m."PlatformRegion"
                    WHERE m."Status" = @successStatus
                      AND m."Patch" IN (SELECT "Version" FROM slice_lol_patch_versions)
                )
                INSERT INTO slice_lol_match_ids ("Id")
                SELECT "Id"
                FROM candidates
                WHERE row_number <= LEAST(
                    @maxMatchesPerRegion,
                    GREATEST(1, CEIL(total_in_region * @samplePercent / 100.0)::int));
                """,
                new NpgsqlParameter("successStatus", 1),
                new NpgsqlParameter("maxMatchesPerRegion", options.LolMaxMatchesPerRegion),
                new NpgsqlParameter("samplePercent", options.LolSamplePercent));

            await ExecuteNonQueryAsync(source, transaction, $"""
                INSERT INTO slice_lol_summoner_ids ("Id")
                SELECT DISTINCT mp."SummonerId"
                FROM {matchParticipants} mp
                JOIN slice_lol_match_ids ids ON ids."Id" = mp."MatchId";
                """);

            await ExecuteNonQueryAsync(source, transaction, $"""
                INSERT INTO slice_lol_participant_ids ("Id")
                SELECT mp."Id"
                FROM {matchParticipants} mp
                JOIN slice_lol_match_ids ids ON ids."Id" = mp."MatchId";
                """);
        }

        if (!options.SkipTft)
        {
            await ExecuteNonQueryAsync(
                source,
                transaction,
                $"""
                INSERT INTO slice_tft_patch_versions ("Version")
                SELECT "Version"
                FROM {tftPatches}
                ORDER BY CASE WHEN "IsActive" THEN 0 ELSE 1 END, "DetectedAtUtc" DESC
                LIMIT @patchDepth;
                """,
                new NpgsqlParameter("patchDepth", options.PatchDepth));

            await ExecuteNonQueryAsync(
                source,
                transaction,
                $"""
                WITH candidates AS (
                    SELECT
                        m."Id",
                        m."PlatformRegion",
                        ROW_NUMBER() OVER (
                            PARTITION BY m."PlatformRegion"
                            ORDER BY m."MatchDate" DESC, m."Id") AS row_number,
                        COUNT(*) OVER (PARTITION BY m."PlatformRegion") AS total_in_region
                    FROM {tftMatches} m
                    JOIN slice_regions r ON r."Region" = m."PlatformRegion"
                    WHERE m."Status" = @successStatus
                      AND m."Patch" IN (SELECT "Version" FROM slice_tft_patch_versions)
                )
                INSERT INTO slice_tft_match_ids ("Id")
                SELECT "Id"
                FROM candidates
                WHERE row_number <= LEAST(
                    @maxMatchesPerRegion,
                    GREATEST(1, CEIL(total_in_region * @samplePercent / 100.0)::int));
                """,
                new NpgsqlParameter("successStatus", 1),
                new NpgsqlParameter("maxMatchesPerRegion", options.TftMaxMatchesPerRegion),
                new NpgsqlParameter("samplePercent", options.TftSamplePercent));

            await ExecuteNonQueryAsync(source, transaction, $"""
                INSERT INTO slice_tft_summoner_ids ("Id")
                SELECT DISTINCT mp."SummonerId"
                FROM {tftParticipants} mp
                JOIN slice_tft_match_ids ids ON ids."Id" = mp."MatchId";
                """);

            await ExecuteNonQueryAsync(source, transaction, $"""
                INSERT INTO slice_tft_participant_ids ("Id")
                SELECT mp."Id"
                FROM {tftParticipants} mp
                JOIN slice_tft_match_ids ids ON ids."Id" = mp."MatchId";
                """);
        }
    }

    private static IReadOnlyList<CopyDefinition> BuildCopyPlan(SliceSyncOptions options)
    {
        var plan = new List<CopyDefinition>();
        var patches = QuoteIdentifier(SchemaTables.For<Patch>());
        var currentDataParameters = QuoteIdentifier(SchemaTables.For<CurrentDataParameters>());
        var currentChampionLoadouts = QuoteIdentifier(SchemaTables.For<CurrentChampionLoadout>());
        var runeVersions = QuoteIdentifier(SchemaTables.For<RuneVersion>());
        var itemVersions = QuoteIdentifier(SchemaTables.For<ItemVersion>());
        var summoners = QuoteIdentifier(SchemaTables.For<Summoner>());
        var trackedPros = QuoteIdentifier(SchemaTables.For<TrackedProSummoner>());
        var ranks = QuoteIdentifier(SchemaTables.For<Rank>());
        var historicalRanks = QuoteIdentifier(SchemaTables.For<HistoricalRank>());
        var summonerIngestionCursors = QuoteIdentifier(SchemaTables.For<SummonerIngestionCursor>());
        var liveSnapshots = QuoteIdentifier(SchemaTables.For<LiveGameSnapshot>());
        var matches = QuoteIdentifier(SchemaTables.For<Match>());
        var matchBans = QuoteIdentifier(SchemaTables.For<MatchBan>());
        var timelineFetchStates = QuoteIdentifier(SchemaTables.For<MatchTimelineFetchState>());
        var timelineSnapshots = QuoteIdentifier(SchemaTables.For<MatchParticipantTimelineSnapshot>());
        var matchParticipants = QuoteIdentifier(SchemaTables.For<MatchParticipant>());
        var matchParticipantItems = QuoteIdentifier(SchemaTables.For<MatchParticipantItem>());
        var matchParticipantRunes = QuoteIdentifier(SchemaTables.For<MatchParticipantRune>());
        var tftPatches = QuoteIdentifier(SchemaTables.For<TftPatch>());
        var tftSets = QuoteIdentifier(SchemaTables.For<TftSet>());
        var tftUnitVersions = QuoteIdentifier(SchemaTables.For<TftUnitVersion>());
        var tftItemVersions = QuoteIdentifier(SchemaTables.For<TftItemVersion>());
        var tftTraitVersions = QuoteIdentifier(SchemaTables.For<TftTraitVersion>());
        var tftAugmentVersions = QuoteIdentifier(SchemaTables.For<TftAugmentVersion>());
        var tftSummoners = QuoteIdentifier(SchemaTables.For<TftSummoner>());
        var tftRanks = QuoteIdentifier(SchemaTables.For<TftRank>());
        var tftHistoricalRanks = QuoteIdentifier(SchemaTables.For<TftHistoricalRank>());
        var tftSummonerIngestionCursors = QuoteIdentifier(SchemaTables.For<TftSummonerIngestionCursor>());
        var tftMatches = QuoteIdentifier(SchemaTables.For<TftMatch>());
        var tftParticipants = QuoteIdentifier(SchemaTables.For<TftMatchParticipant>());
        var tftUnits = QuoteIdentifier(SchemaTables.For<TftMatchParticipantUnit>());
        var tftTraits = QuoteIdentifier(SchemaTables.For<TftMatchParticipantTrait>());
        var tftAugments = QuoteIdentifier(SchemaTables.For<TftMatchParticipantAugment>());
        var matchSummoners = QuoteIdentifier(SchemaTables.ForManyToMany<Match, Summoner>());

        if (!options.SkipLol)
        {
            plan.Add(For<Patch>($"SELECT * FROM {patches};"));
            plan.Add(For<CurrentDataParameters>($"SELECT * FROM {currentDataParameters};"));
            plan.Add(For<CurrentChampionLoadout>($"""
                SELECT *
                FROM {currentChampionLoadouts}
                WHERE "Patch" IN (SELECT "Version" FROM slice_lol_patch_versions);
                """));
            plan.Add(For<RuneVersion>($"SELECT * FROM {runeVersions};"));
            plan.Add(For<ItemVersion>($"SELECT * FROM {itemVersions};"));
            plan.Add(For<Summoner>($"""
                SELECT s.*
                FROM {summoners} s
                JOIN slice_lol_summoner_ids ids ON ids."Id" = s."Id";
                """));
            plan.Add(For<TrackedProSummoner>($"""
                SELECT p.*
                FROM {trackedPros} p
                JOIN {summoners} s
                  ON s."Puuid" = p."Puuid" AND s."PlatformRegion" = p."PlatformRegion"
                JOIN slice_lol_summoner_ids ids ON ids."Id" = s."Id";
                """));
            plan.Add(For<Rank>($"""
                SELECT r.*
                FROM {ranks} r
                JOIN slice_lol_summoner_ids ids ON ids."Id" = r."SummonerId";
                """));
            plan.Add(For<HistoricalRank>($"""
                SELECT r.*
                FROM {historicalRanks} r
                JOIN slice_lol_summoner_ids ids ON ids."Id" = r."SummonerId";
                """));
            plan.Add(For<SummonerIngestionCursor>($"""
                SELECT c.*
                FROM {summonerIngestionCursors} c
                JOIN slice_lol_summoner_ids ids ON ids."Id" = c."SummonerId";
                """));
            plan.Add(For<LiveGameSnapshot>($"""
                SELECT s.*
                FROM {liveSnapshots} s
                JOIN slice_lol_summoner_ids ids ON ids."Id" = s."SummonerId";
                """));
            plan.Add(For<Match>($"""
                SELECT m.*
                FROM {matches} m
                JOIN slice_lol_match_ids ids ON ids."Id" = m."Id";
                """));
            plan.Add(new CopyDefinition(SchemaTables.ForManyToMany<Match, Summoner>(), $"""
                SELECT ms.*
                FROM {matchSummoners} ms
                JOIN slice_lol_match_ids m ON m."Id" = ms."MatchId"
                JOIN slice_lol_summoner_ids s ON s."Id" = ms."SummonerId";
                """));
            plan.Add(For<MatchBan>($"""
                SELECT b.*
                FROM {matchBans} b
                JOIN slice_lol_match_ids ids ON ids."Id" = b."MatchId";
                """));
            plan.Add(For<MatchTimelineFetchState>($"""
                SELECT s.*
                FROM {timelineFetchStates} s
                JOIN slice_lol_match_ids ids ON ids."Id" = s."MatchId";
                """));
            plan.Add(For<MatchParticipantTimelineSnapshot>($"""
                SELECT s.*
                FROM {timelineSnapshots} s
                JOIN slice_lol_match_ids ids ON ids."Id" = s."MatchId";
                """));
            plan.Add(For<MatchParticipant>($"""
                SELECT p.*
                FROM {matchParticipants} p
                JOIN slice_lol_participant_ids ids ON ids."Id" = p."Id";
                """));
            plan.Add(For<MatchParticipantItem>($"""
                SELECT i.*
                FROM {matchParticipantItems} i
                JOIN slice_lol_participant_ids ids ON ids."Id" = i."MatchParticipantId";
                """));
            plan.Add(For<MatchParticipantRune>($"""
                SELECT r.*
                FROM {matchParticipantRunes} r
                JOIN slice_lol_participant_ids ids ON ids."Id" = r."MatchParticipantId";
                """));
        }

        if (!options.SkipTft)
        {
            plan.Add(For<TftPatch>($"SELECT * FROM {tftPatches};"));
            plan.Add(For<TftSet>($"SELECT * FROM {tftSets};"));
            plan.Add(For<TftUnitVersion>($"SELECT * FROM {tftUnitVersions};"));
            plan.Add(For<TftItemVersion>($"SELECT * FROM {tftItemVersions};"));
            plan.Add(For<TftTraitVersion>($"SELECT * FROM {tftTraitVersions};"));
            plan.Add(For<TftAugmentVersion>($"SELECT * FROM {tftAugmentVersions};"));
            plan.Add(For<TftSummoner>($"""
                SELECT s.*
                FROM {tftSummoners} s
                JOIN slice_tft_summoner_ids ids ON ids."Id" = s."Id";
                """));
            plan.Add(For<TftRank>($"""
                SELECT r.*
                FROM {tftRanks} r
                JOIN slice_tft_summoner_ids ids ON ids."Id" = r."SummonerId";
                """));
            plan.Add(For<TftHistoricalRank>($"""
                SELECT r.*
                FROM {tftHistoricalRanks} r
                JOIN slice_tft_summoner_ids ids ON ids."Id" = r."SummonerId";
                """));
            plan.Add(For<TftSummonerIngestionCursor>($"""
                SELECT c.*
                FROM {tftSummonerIngestionCursors} c
                JOIN slice_tft_summoner_ids ids ON ids."Id" = c."SummonerId";
                """));
            plan.Add(For<TftMatch>($"""
                SELECT m.*
                FROM {tftMatches} m
                JOIN slice_tft_match_ids ids ON ids."Id" = m."Id";
                """));
            plan.Add(For<TftMatchParticipant>($"""
                SELECT p.*
                FROM {tftParticipants} p
                JOIN slice_tft_participant_ids ids ON ids."Id" = p."Id";
                """));
            plan.Add(For<TftMatchParticipantUnit>($"""
                SELECT u.*
                FROM {tftUnits} u
                JOIN slice_tft_participant_ids ids ON ids."Id" = u."ParticipantId";
                """));
            plan.Add(For<TftMatchParticipantTrait>($"""
                SELECT t.*
                FROM {tftTraits} t
                JOIN slice_tft_participant_ids ids ON ids."Id" = t."ParticipantId";
                """));
            plan.Add(For<TftMatchParticipantAugment>($"""
                SELECT a.*
                FROM {tftAugments} a
                JOIN slice_tft_participant_ids ids ON ids."Id" = a."ParticipantId";
                """));
        }

        return plan;
    }

    private static async Task<long> CopyTableAsync(
        NpgsqlConnection source,
        NpgsqlTransaction sourceTransaction,
        NpgsqlConnection target,
        NpgsqlTransaction targetTransaction,
        string tableName,
        string sourceQuery)
    {
        await using var sourceCommand = new NpgsqlCommand(sourceQuery, source, sourceTransaction)
        {
            CommandTimeout = 0
        };

        await using var reader = await sourceCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var columns = reader.GetColumnSchema()
            .Select(column => column.ColumnName)
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Cast<string>()
            .ToArray();

        if (columns.Length == 0)
            return 0;

        var copyCommand =
            $"COPY {QuoteIdentifier(tableName)} ({string.Join(", ", columns.Select(QuoteIdentifier))}) FROM STDIN (FORMAT BINARY)";
        await using var importer = target.BeginBinaryImport(copyCommand);

        long rows = 0;
        while (await reader.ReadAsync())
        {
            await importer.StartRowAsync();
            for (var index = 0; index < columns.Length; index++)
            {
                if (await reader.IsDBNullAsync(index))
                    await importer.WriteNullAsync();
                else
                    await importer.WriteAsync(reader.GetValue(index));
            }

            rows++;
        }

        await importer.CompleteAsync();
        return rows;
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 0
        };
        if (parameters.Length > 0)
            command.Parameters.AddRange(parameters);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T?> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var result = await command.ExecuteScalarAsync();
        return result is T typed ? typed : default;
    }

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static CopyDefinition For<TEntity>(string sourceQuery)
        where TEntity : class
        => new(SchemaTables.For<TEntity>(), sourceQuery);

    private sealed record CopyDefinition(string TableName, string SourceQuery);

    private sealed record SliceSyncOptions(
        string SourceConnectionString,
        string TargetConnectionString,
        string[] Regions,
        int PatchDepth,
        int LolMaxMatchesPerRegion,
        double LolSamplePercent,
        int TftMaxMatchesPerRegion,
        double TftSamplePercent,
        bool SkipLol,
        bool SkipTft)
    {
        public static SliceSyncOptions FromArgs(CommandArguments args)
        {
            var sourceConnectionString = ResolveConnectionString(args.GetString("source"), "TRN_SOURCE_DB");
            var targetConnectionString = ResolveConnectionString(args.GetString("target"), "TRN_TARGET_DB")
                                         ?? "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=changme";

            return new SliceSyncOptions(
                sourceConnectionString ?? string.Empty,
                targetConnectionString,
                NormalizeRegions(args.GetCsv("regions", DefaultRegions)),
                args.GetInt("patch-depth", 2),
                args.GetInt("lol-max-matches-per-region", 5000),
                args.GetDouble("lol-sample-percent", 100),
                args.GetInt("tft-max-matches-per-region", 3000),
                args.GetDouble("tft-sample-percent", 100),
                args.HasFlag("skip-lol"),
                args.HasFlag("skip-tft"));
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SourceConnectionString))
                throw new InvalidOperationException("A source connection string is required via --source or TRN_SOURCE_DB.");

            if (string.IsNullOrWhiteSpace(TargetConnectionString))
                throw new InvalidOperationException("A target connection string is required via --target or TRN_TARGET_DB.");

            if (Regions.Length == 0)
                throw new InvalidOperationException("At least one platform region is required.");

            if (PatchDepth <= 0)
                throw new InvalidOperationException("--patch-depth must be greater than 0.");

            if (LolMaxMatchesPerRegion <= 0 || TftMaxMatchesPerRegion <= 0)
                throw new InvalidOperationException("Max matches per region must be greater than 0.");

            if (LolSamplePercent <= 0 || LolSamplePercent > 100 ||
                TftSamplePercent <= 0 || TftSamplePercent > 100)
                throw new InvalidOperationException("Sample percentages must be within (0, 100].");

            if (SkipLol && SkipTft)
                throw new InvalidOperationException("At least one of LoL or TFT must remain enabled.");
        }

        private static string[] NormalizeRegions(IEnumerable<string> values)
        {
            return values.Select(value =>
                {
                    if (!PlatformRouteParser.TryParse(value, out var platform))
                        throw new InvalidOperationException($"Unsupported platform region '{value}'.");
                    return platform.ToString();
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string? ResolveConnectionString(string? explicitValue, string environmentKey)
        {
            return !string.IsNullOrWhiteSpace(explicitValue)
                ? explicitValue
                : Environment.GetEnvironmentVariable(environmentKey);
        }
    }
}
