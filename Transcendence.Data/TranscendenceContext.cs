using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Models.Service;

namespace Transcendence.Data;

public class TranscendenceContext(DbContextOptions<TranscendenceContext> options) : DbContext(options)
{
    public DbSet<Summoner> Summoners { get; set; }
    public DbSet<Match> Matches { get; set; }
    public DbSet<CurrentDataParameters> CurrentDataParameters { get; set; }
    public DbSet<Rank> Ranks { get; set; }
    public DbSet<HistoricalRank> HistoricalRanks { get; set; }
    public DbSet<ChampionMastery> ChampionMasteries { get; set; }
    public DbSet<TrackedProSummoner> TrackedProSummoners { get; set; }
    public DbSet<SummonerIngestionCursor> SummonerIngestionCursors { get; set; }
    public DbSet<RankedSeason> RankedSeasons { get; set; }
    public DbSet<SummonerFullHistoryBackfill> SummonerFullHistoryBackfills { get; set; }
    public DbSet<SummonerMatchFact> SummonerMatchFacts { get; set; }
    public DbSet<SummonerMatchFactFetchFailure> SummonerMatchFactFetchFailures { get; set; }
    public DbSet<SummonerSeasonOverviewStat> SummonerSeasonOverviewStats { get; set; }
    public DbSet<SummonerSeasonChampionStat> SummonerSeasonChampionStats { get; set; }
    public DbSet<SummonerSeasonCoverage> SummonerSeasonCoverages { get; set; }
    public DbSet<CurrentChampionLoadout> CurrentChampionLoadouts { get; set; }
    public DbSet<MatchParticipant> MatchParticipants { get; set; }
    public DbSet<MatchBan> MatchBans { get; set; }
    public DbSet<MatchTeamObjective> MatchTeamObjectives { get; set; }
    public DbSet<MatchTimelineFetchState> MatchTimelineFetchStates { get; set; }
    public DbSet<MatchParticipantTimelineSnapshot> MatchParticipantTimelineSnapshots { get; set; }
    public DbSet<MatchParticipantItemPurchase> MatchParticipantItemPurchases { get; set; }
    public DbSet<MatchParticipantSkillOrder> MatchParticipantSkillOrders { get; set; }

    // Precomputed analytics aggregates (refreshed on a cadence; the read path rolls these up by scope)
    public DbSet<ChampionRoleTierStat> ChampionRoleTierStats { get; set; }
    public DbSet<ScopeMatchCountStat> ScopeMatchCountStats { get; set; }
    public DbSet<ChampionBanScopeStat> ChampionBanScopeStats { get; set; }
    public DbSet<ChampionScopeGradeStat> ChampionScopeGradeStats { get; set; }
    public DbSet<ChampionMatchupStat> ChampionMatchupStats { get; set; }
    public DbSet<ChampionBuildSnapshot> ChampionBuildSnapshots { get; set; }
    public DbSet<BuildResourceSnapshot> BuildResourceSnapshots { get; set; }
    public DbSet<BuildResourceStat> BuildResourceStats { get; set; }
    public DbSet<BuildResourcePopulationStat> BuildResourcePopulationStats { get; set; }
    public DbSet<BuildResourceProcessedMatch> BuildResourceProcessedMatches { get; set; }
    public DbSet<AnalyticsResponseSnapshot> AnalyticsResponseSnapshots { get; set; }

    // Versioned static data
    public DbSet<Patch> Patches { get; set; }
    public DbSet<RuneVersion> RuneVersions { get; set; }
    public DbSet<ItemVersion> ItemVersions { get; set; }

    // Join tables for match participants
    public DbSet<MatchParticipantRune> MatchParticipantRunes { get; set; }
    public DbSet<MatchParticipantItem> MatchParticipantItems { get; set; }

    public DbSet<RefreshLock> RefreshLocks { get; set; }
    public DbSet<ApiClientKey> ApiClientKeys { get; set; }
    public DbSet<UserAccount> UserAccounts { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<UserRefreshToken> UserRefreshTokens { get; set; }
    public DbSet<UserPasswordResetToken> UserPasswordResetTokens { get; set; }
    public DbSet<UserFavoriteSummoner> UserFavoriteSummoners { get; set; }
    public DbSet<UserPreferences> UserPreferences { get; set; }
    public DbSet<UserRiotAccount> UserRiotAccounts { get; set; }
    public DbSet<AdminAuditEvent> AdminAuditEvents { get; set; }
    public DbSet<LiveGameSnapshot> LiveGameSnapshots { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (!await TryMergeMonotonicSummonerActivityAsync(exception, cancellationToken))
                throw;

            // One bounded retry: a second collision is allowed to fail fast and be retried by the
            // surrounding job. Only LastActiveAtUtc is mergeable; profile fields never silently win.
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<bool> TryMergeMonotonicSummonerActivityAsync(
        DbUpdateConcurrencyException exception,
        CancellationToken cancellationToken)
    {
        const string activityProperty = nameof(Summoner.LastActiveAtUtc);
        foreach (var entry in exception.Entries)
        {
            if (entry.Entity is not Summoner || entry.State != EntityState.Modified)
                return false;

            var modifiedProperties = entry.Properties
                .Where(property => property.IsModified)
                .Select(property => property.Metadata.Name)
                .ToList();
            if (modifiedProperties.Count != 1 || modifiedProperties[0] != activityProperty)
                return false;
        }

        foreach (var entry in exception.Entries)
        {
            var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
            if (databaseValues == null)
                return false;

            var proposedActivity = entry.CurrentValues.GetValue<DateTime?>(activityProperty);
            var databaseActivity = databaseValues.GetValue<DateTime?>(activityProperty);
            var shouldAdvance = proposedActivity.HasValue
                                && (!databaseActivity.HasValue || proposedActivity.Value > databaseActivity.Value);
            var mergedActivity = shouldAdvance ? proposedActivity : databaseActivity;

            entry.OriginalValues.SetValues(databaseValues);
            entry.CurrentValues.SetValues(databaseValues);
            entry.CurrentValues[activityProperty] = mergedActivity;
            entry.Property(activityProperty).IsModified = shouldAdvance;
        }

        return true;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rank>()
            .HasIndex(x => new
            {
                x.SummonerId,
                x.QueueType
            })
            .IsUnique();

        modelBuilder.Entity<Match>()
            .Property(x => x.MatchId)
            .IsRequired();

        modelBuilder.Entity<Match>()
            .HasIndex(x => new
            {
                x.MatchId
            })
            .IsUnique();

        // Helpful secondary indexes for query patterns on matches
        modelBuilder.Entity<Match>()
            .HasIndex(x => x.MatchDate);
        modelBuilder.Entity<Match>()
            .HasIndex(x => x.QueueType);
        modelBuilder.Entity<Match>()
            .HasIndex(x => x.QueueId);
        modelBuilder.Entity<Match>()
            .HasIndex(x => x.QueueFamily);
        modelBuilder.Entity<Match>()
            .HasIndex(x => new { x.PlatformRegion, x.Status, x.Patch });

        // Summoner lookups by Puuid
        modelBuilder.Entity<Summoner>()
            .Property(s => s.Puuid)
            .IsRequired();

        var summonerVersion = modelBuilder.Entity<Summoner>()
            .Property(s => s.Version)
            .IsConcurrencyToken();
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            summonerVersion.IsRowVersion();
        else
            summonerVersion.ValueGeneratedNever();

        modelBuilder.Entity<Summoner>()
            .HasIndex(s => s.Puuid)
            .IsUnique();

        // Riot riot-ids (gameName#tagLine) are mutable and reusable, so they are NOT a stable
        // unique identity — the PUUID is (IX_Summoners_Puuid, unique). This is therefore a
        // NON-unique lookup index for search/refresh; FindByRiotIdsAsync dedupes to the most
        // recently updated row. (A unique constraint here caused upsert collisions when match
        // data carried a historical name now held by a different PUUID.)
        modelBuilder.Entity<Summoner>()
            .HasIndex(s => new { s.PlatformRegion, s.GameNameNormalized, s.TagLineNormalized })
            .HasDatabaseName("IX_Summoners_SearchPrefix")
            .HasFilter("\"GameNameNormalized\" IS NOT NULL AND \"TagLineNormalized\" IS NOT NULL");

        // Drives per-region ingestion candidate selection: the coverage-cooldown / staleness filter
        // (UpdatedAt <= cutoff, ordered by UpdatedAt) and the oldest-eligible MinAsync(UpdatedAt) the
        // producers run each tick. Previously unindexed → sequential scans over the whole Summoners table.
        // NOTE: in prod this is built with CREATE INDEX CONCURRENTLY (applied via psql, then the
        // migration is recorded as applied) so the large live table is not write-locked.
        modelBuilder.Entity<Summoner>()
            .HasIndex(s => new { s.PlatformRegion, s.UpdatedAt })
            .HasDatabaseName("IX_Summoners_Region_UpdatedAt");

        // Partial index over only ACTIVE summoners (LastActiveAtUtc set), supporting activity-aware
        // candidate selection (PreferActiveSummoners): WHERE PlatformRegion=@r AND LastActiveAtUtc IS
        // NOT NULL ORDER BY UpdatedAt. Excludes the large inert tail, so the ordered scan never pages
        // through the MinValue stubs. NOTE: built with CREATE INDEX CONCURRENTLY out-of-band in prod.
        modelBuilder.Entity<Summoner>()
            .HasIndex(s => new { s.PlatformRegion, s.UpdatedAt }, "IX_Summoners_Region_UpdatedAt_Active")
            .HasFilter("\"LastActiveAtUtc\" IS NOT NULL");

        // Global query filter to exclude unfetchable matches from normal queries
        modelBuilder.Entity<Match>()
            .HasQueryFilter(m => m.Status != FetchStatus.PermanentlyUnfetchable);

        // Apply matching filter to dependents to avoid required-parent filter warning
        modelBuilder.Entity<MatchParticipant>()
            .HasQueryFilter(mp => mp.Match.Status != FetchStatus.PermanentlyUnfetchable);

        // MatchParticipant configuration
        modelBuilder.Entity<MatchParticipant>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.HasOne(p => p.Match)
                .WithMany(m => m.Participants)
                .HasForeignKey(p => p.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Summoner)
                .WithMany(s => s.MatchParticipants)
                .HasForeignKey(p => p.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Enforce one participant per (Match, Summoner)
            entity.HasIndex(p => new
            {
                p.MatchId,
                p.SummonerId
            })
                .IsUnique();

            // Common filter/index fields
            entity.HasIndex(p => p.SummonerId);
            // Covering index for the dominant champion-analytics query
            // (ChampionWinRateComputeService.ComputeWinRatesAsync): seek by ChampionId and read
            // the join + aggregate payload (MatchId for the Match join, SummonerId for the Ranks
            // join, TeamPosition + Win for the grouping) straight from the index leaf — turning the
            // ~48k-block heap fetch into an index-only scan (prod EXPLAIN: the MatchParticipants
            // access dropped from ~150ms to ~16ms). The INCLUDE columns are Npgsql-specific and this
            // project references only EF.Relational, so they live in the raw-SQL migration; the model
            // declares the bare (ChampionId) shape under the same name. Built CONCURRENTLY on the hot
            // MatchParticipants table out-of-band — see docs/DEVELOPMENT.md "Applying index migrations
            // to hot tables". This also covers a plain (ChampionId) seek, so no separate index is kept.
            entity.HasIndex(p => p.ChampionId, "IX_MatchParticipants_ChampionId_Covering");
            // Covering index for the champion-side scan of the matchup query
            // (ChampionMatchupComputeService.ComputeMatchupsAsync): seek by (ChampionId, TeamPosition)
            // and read MatchId/ParticipantId/Win/TeamId from the leaf so the scan is index-only (prod
            // EXPLAIN: it was a ~50k-cost bitmap heap scan). Bare shape in the model; INCLUDE in the
            // raw-SQL migration. Replaces the plain non-covering (ChampionId, TeamPosition) index.
            entity.HasIndex(p => new { p.ChampionId, p.TeamPosition },
                "IX_MatchParticipants_ChampionId_TeamPosition_Covering");
            // Covering index for the lane-pairs self-join of the matchup query (the opponent lookup):
            // seek by MatchId and read TeamPosition/TeamId/ChampionId/ParticipantId from the leaf so the
            // self-join is index-only (prod EXPLAIN: the single biggest cost, ~+72k of heap fetches).
            // Also serves the MatchId FK and every MatchId lookup (the hottest index, ~131M scans/day).
            // Bare (MatchId) shape in the model; INCLUDE in the raw-SQL migration. Replaces the plain index.
            entity.HasIndex(p => p.MatchId, "IX_MatchParticipants_MatchId_Covering");
            entity.HasIndex(p => new { p.MatchId, p.ParticipantId });
        });

        // Versioned static data configuration
        modelBuilder.Entity<Patch>(entity => { entity.HasKey(p => p.Version); });

        // RefreshLock configuration
        modelBuilder.Entity<RefreshLock>(entity =>
        {
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => x.LockedUntilUtc);
        });

        // API key authentication
        modelBuilder.Entity<ApiClientKey>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.KeyHash).IsUnique();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.KeyHash).IsRequired();
            entity.Property(x => x.KeyPrefix).IsRequired();
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EmailNormalized).IsUnique();
            entity.Property(x => x.Email).IsRequired();
            entity.Property(x => x.EmailNormalized).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(128);
        });

        modelBuilder.Entity<UserRiotAccount>(entity =>
        {
            entity.HasKey(x => x.UserAccountId);
            entity.HasIndex(x => x.Puuid).IsUnique();
            entity.Property(x => x.Puuid).HasMaxLength(128).IsRequired();
            entity.Property(x => x.GameName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TagLine).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PlatformRegion).HasMaxLength(16).IsRequired();
            entity.HasOne(x => x.UserAccount)
                .WithOne(x => x.RiotAccount)
                .HasForeignKey<UserRiotAccount>(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(x => new { x.UserAccountId, x.Role });
            entity.Property(x => x.Role).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.Role);
            entity.HasOne(x => x.UserAccount)
                .WithMany(x => x.Roles)
                .HasForeignKey(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserAccountId, x.ExpiresAtUtc });

            entity.HasOne(x => x.UserAccount)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPasswordResetToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserAccountId, x.ExpiresAtUtc });
            entity.Property(x => x.TokenHash).IsRequired();

            entity.HasOne(x => x.UserAccount)
                .WithMany(x => x.PasswordResetTokens)
                .HasForeignKey(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserFavoriteSummoner>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserAccountId, x.SummonerPuuid, x.PlatformRegion }).IsUnique();
            entity.Property(x => x.SummonerPuuid).IsRequired();
            entity.Property(x => x.PlatformRegion).IsRequired();

            entity.HasOne(x => x.UserAccount)
                .WithMany(x => x.FavoriteSummoners)
                .HasForeignKey(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPreferences>(entity =>
        {
            entity.HasKey(x => x.UserAccountId);
            entity.HasOne(x => x.UserAccount)
                .WithOne(x => x.Preferences)
                .HasForeignKey<UserPreferences>(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdminAuditEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).IsRequired().HasMaxLength(128);
            entity.Property(x => x.TargetType).HasMaxLength(128);
            entity.Property(x => x.TargetId).HasMaxLength(256);
            entity.Property(x => x.RequestId).HasMaxLength(128);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => new { x.ActorUserAccountId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.Action, x.CreatedAtUtc });
        });

        modelBuilder.Entity<LiveGameSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Puuid, x.PlatformRegion, x.ObservedAtUtc });
            entity.HasIndex(x => x.NextPollAtUtc);
            entity.Property(x => x.State).IsRequired();
            entity.Property(x => x.PlatformRegion).IsRequired();
            entity.Property(x => x.Puuid).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<RuneVersion>(entity =>
        {
            entity.HasKey(rv => new
            {
                rv.RuneId,
                rv.PatchVersion
            });

            entity.HasOne(rv => rv.Patch)
                .WithMany()
                .HasForeignKey(rv => rv.PatchVersion);
        });

        modelBuilder.Entity<ItemVersion>(entity =>
        {
            entity.HasKey(iv => new
            {
                iv.ItemId,
                iv.PatchVersion
            });

            entity.Property(iv => iv.BuildsFrom)
                .HasDefaultValueSql("'{}'::integer[]");
            entity.Property(iv => iv.BuildsInto)
                .HasDefaultValueSql("'{}'::integer[]");
            entity.Property(iv => iv.InStore)
                .HasDefaultValue(true);
            entity.Property(iv => iv.PriceTotal)
                .HasDefaultValue(0);

            entity.HasOne(iv => iv.Patch)
                .WithMany()
                .HasForeignKey(iv => iv.PatchVersion);
        });

        // Match participant join tables configuration
        modelBuilder.Entity<MatchParticipantRune>(entity =>
        {
            entity.HasKey(mpr => new
            {
                mpr.MatchParticipantId,
                mpr.SelectionTree,
                mpr.SelectionIndex,
                mpr.RuneId
            });

            entity.HasOne(mpr => mpr.MatchParticipant)
                .WithMany(mp => mp.Runes)
                .HasForeignKey(mpr => mpr.MatchParticipantId);

            // RuneId + PatchVersion are immutable historical facts. Static-data metadata is
            // intentionally resolved with a soft join so a missing/reseeded RuneVersion cannot
            // reject or cascade-delete match history.
            entity.HasIndex(mpr => new { mpr.RuneId, mpr.PatchVersion });
        });

        modelBuilder.Entity<MatchParticipantRune>()
            .HasQueryFilter(mpr => mpr.MatchParticipant.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<MatchBan>(entity =>
        {
            entity.HasKey(mb => new
            {
                mb.MatchId,
                mb.TeamId,
                mb.PickTurn,
                mb.ChampionId
            });

            entity.HasOne(mb => mb.Match)
                .WithMany(m => m.Bans)
                .HasForeignKey(mb => mb.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(mb => mb.ChampionId);
            entity.HasIndex(mb => new { mb.ChampionId, mb.MatchId });
        });

        modelBuilder.Entity<MatchBan>()
            .HasQueryFilter(mb => mb.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<MatchTeamObjective>(entity =>
        {
            entity.HasKey(o => new { o.MatchId, o.TeamId });
            entity.HasOne(o => o.Match)
                .WithMany(m => m.TeamObjectives)
                .HasForeignKey(o => o.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchTeamObjective>()
            .HasQueryFilter(o => o.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<ChampionMastery>(entity =>
        {
            entity.HasKey(cm => new { cm.SummonerId, cm.ChampionId });
            entity.HasOne(cm => cm.Summoner)
                .WithMany(s => s.ChampionMasteries)
                .HasForeignKey(cm => cm.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchTimelineFetchState>(entity =>
        {
            entity.HasKey(x => x.MatchId);
            entity.HasOne(x => x.Match)
                .WithOne(m => m.TimelineFetchState)
                .HasForeignKey<MatchTimelineFetchState>(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.LastAttemptAtUtc);
        });

        modelBuilder.Entity<MatchTimelineFetchState>()
            .HasQueryFilter(x => x.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<MatchParticipantTimelineSnapshot>(entity =>
        {
            entity.HasKey(x => new { x.MatchId, x.ParticipantId, x.MinuteMark });
            entity.HasOne(x => x.Match)
                .WithMany(m => m.TimelineSnapshots)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.MinuteMark, x.MatchId });
            // Covering index for the matchup gold/xp-diff-at-15 join
            // (ChampionMatchupComputeService.ComputeMatchupsAsync). That query LEFT-joins this
            // 22.5M-row table twice — championTimeline and opponentTimeline — on
            // (MatchId, ParticipantId) with MinuteMark == 15. The key columns mirror the PK, so the
            // join key was already seekable; the win is the INCLUDE payload, which lets both scans run
            // index-only instead of heap-fetching Gold/Xp (both sides) and DerivedAtUtc (champion side,
            // for LatestTimelineAtUtc) per row. On the HDD-backed prod DB that heap fetch pushed the
            // matchup query to ~28s and tripped the 30s command timeout, failing WarmDefaultChampion-
            // ProfilesJob every cycle. INCLUDE (Gold, Xp, DerivedAtUtc) is Npgsql-specific and this
            // project references only EF.Relational, so it lives in the raw-SQL migration; the model
            // declares the bare (MatchId, ParticipantId, MinuteMark) shape under the same name. Built
            // CONCURRENTLY out-of-band on the hot table — see docs/DEVELOPMENT.md "Applying index
            // migrations to hot tables".
            entity.HasIndex(
                x => new { x.MatchId, x.ParticipantId, x.MinuteMark },
                "IX_MatchParticipantTimelineSnapshots_Matchup");
        });

        modelBuilder.Entity<MatchParticipantTimelineSnapshot>()
            .HasQueryFilter(x => x.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<MatchParticipantItemPurchase>(entity =>
        {
            // PK (MatchId, ParticipantId, PurchaseIndex) already serves (MatchId) and
            // (MatchId, ParticipantId) prefix lookups, so no extra secondary index is needed.
            entity.HasKey(x => new { x.MatchId, x.ParticipantId, x.PurchaseIndex });
            entity.HasOne(x => x.Match)
                .WithMany()
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchParticipantItemPurchase>()
            .HasQueryFilter(x => x.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<MatchParticipantSkillOrder>(entity =>
        {
            entity.HasKey(x => new { x.MatchId, x.ParticipantId });
            entity.HasOne(x => x.Match)
                .WithMany()
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchParticipantSkillOrder>()
            .HasQueryFilter(x => x.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<MatchParticipantItem>(entity =>
        {
            entity.HasKey(mpi => new
            {
                mpi.MatchParticipantId,
                mpi.SlotIndex
            });

            entity.HasOne(mpi => mpi.MatchParticipant)
                .WithMany(mp => mp.Items)
                .HasForeignKey(mpi => mpi.MatchParticipantId);

            // ItemId + PatchVersion remain indexed for soft metadata joins, but are not a hard FK:
            // match ingestion must survive partial static-data syncs and static-data reseeds.
            entity.HasIndex(mpi => new { mpi.MatchParticipantId, mpi.ItemId });
            entity.HasIndex(mpi => new { mpi.ItemId, mpi.PatchVersion });

        });

        // Match participant item/rune filters align with match/participant filters
        modelBuilder.Entity<MatchParticipantItem>()
            .HasQueryFilter(mpi => mpi.MatchParticipant.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<TrackedProSummoner>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Puuid).IsRequired();
            entity.Property(x => x.PlatformRegion).IsRequired();
            entity.HasIndex(x => new { x.Puuid, x.PlatformRegion }).IsUnique();
            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => x.UpdatedAtUtc);
        });

        modelBuilder.Entity<SummonerIngestionCursor>(entity =>
        {
            entity.HasKey(x => new { x.SummonerId, x.Scope });
            entity.Property(x => x.Scope).HasMaxLength(64);
            entity.HasOne(x => x.Summoner)
                .WithMany(s => s.IngestionCursors)
                .HasForeignKey(x => x.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.UpdatedAtUtc);
        });

        modelBuilder.Entity<RankedSeason>(entity =>
        {
            entity.HasKey(x => x.SeasonKey);
            entity.Property(x => x.SeasonKey).HasMaxLength(16);
            entity.Property(x => x.DisplayName).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => new { x.StartUtc, x.EndUtc });
        });

        modelBuilder.Entity<SummonerFullHistoryBackfill>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Scope).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastErrorMessage).HasMaxLength(1024);
            entity.HasIndex(x => new { x.SummonerId, x.Scope }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.UpdatedAtUtc });
            entity.HasOne(x => x.Summoner)
                .WithMany(x => x.FullHistoryBackfills)
                .HasForeignKey(x => x.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SummonerMatchFact>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MatchId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Puuid).HasMaxLength(128).IsRequired();
            entity.Property(x => x.PlatformRegion).HasMaxLength(16);
            entity.Property(x => x.RegionalRoute).HasMaxLength(16);
            entity.Property(x => x.SeasonKey).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Patch).HasMaxLength(32);
            entity.Property(x => x.QueueType).HasMaxLength(64);
            entity.Property(x => x.QueueFamily).HasMaxLength(64);
            entity.Property(x => x.EndOfGameResult).HasMaxLength(64);
            entity.Property(x => x.TeamPosition).HasMaxLength(32);
            entity.Property(x => x.IndividualPosition).HasMaxLength(32);
            entity.Property(x => x.RankedCountExclusionReason).HasMaxLength(128);
            entity.HasIndex(x => new { x.SummonerId, x.MatchId }).IsUnique();
            entity.HasIndex(x => new { x.SummonerId, x.SeasonKey, x.QueueId, x.CountsTowardRankedTotal });
            entity.HasIndex(x => new { x.SummonerId, x.SeasonKey, x.QueueFamily });
            entity.HasIndex(x => new { x.MatchDate, x.QueueId });
            entity.HasOne(x => x.Summoner)
                .WithMany(x => x.MatchFacts)
                .HasForeignKey(x => x.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SummonerMatchFactFetchFailure>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MatchId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PlatformRegion).HasMaxLength(16);
            entity.Property(x => x.RegionalRoute).HasMaxLength(16);
            entity.Property(x => x.LastErrorMessage).HasMaxLength(1024);
            entity.HasIndex(x => new { x.SummonerId, x.MatchId }).IsUnique();
            entity.HasIndex(x => new { x.ResolvedAtUtc, x.LastAttemptAtUtc });
            entity.HasOne(x => x.Summoner)
                .WithMany()
                .HasForeignKey(x => x.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SummonerSeasonOverviewStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SeasonKey).HasMaxLength(16).IsRequired();
            entity.Property(x => x.QueueScope).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.SummonerId, x.SeasonKey, x.QueueScope }).IsUnique();
            entity.HasOne(x => x.Summoner)
                .WithMany(x => x.SeasonOverviewStats)
                .HasForeignKey(x => x.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SummonerSeasonChampionStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SeasonKey).HasMaxLength(16).IsRequired();
            entity.Property(x => x.QueueScope).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.SummonerId, x.SeasonKey, x.QueueScope, x.ChampionId }).IsUnique();
            entity.HasIndex(x => new { x.SummonerId, x.SeasonKey, x.QueueScope, x.Games });
            entity.HasOne(x => x.Summoner)
                .WithMany(x => x.SeasonChampionStats)
                .HasForeignKey(x => x.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SummonerSeasonCoverage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SeasonKey).HasMaxLength(16).IsRequired();
            entity.Property(x => x.QueueScope).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BackfillStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CoverageStatus).HasMaxLength(64);
            entity.HasIndex(x => new { x.SummonerId, x.SeasonKey, x.QueueScope }).IsUnique();
            entity.HasOne(x => x.Summoner)
                .WithMany(x => x.SeasonCoverages)
                .HasForeignKey(x => x.SummonerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Precomputed analytics aggregates. Keys double as the UPSERT conflict target and the read
        // lookup; the secondary indexes serve the two read shapes (per-champion vs across-all-champions).
        modelBuilder.Entity<ChampionRoleTierStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Patch).HasMaxLength(32);
            entity.Property(x => x.QueueFamily).HasMaxLength(64).HasDefaultValue("RANKED_SOLO_DUO");
            entity.Property(x => x.PlatformRegion).HasMaxLength(16);
            entity.Property(x => x.RankTier).HasMaxLength(32);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.HasIndex(x => new { x.Patch, x.QueueFamily, x.PlatformRegion, x.RankTier, x.ChampionId, x.Role })
                .IsUnique();
            // Win-rate read: a single champion across its roles/tiers/regions.
            entity.HasIndex(x => new { x.Patch, x.QueueFamily, x.ChampionId, x.Role });
            // Tier-list + role-rank/pick-rate population read: every champion in a role/tier/region scope.
            entity.HasIndex(x => new { x.Patch, x.QueueFamily, x.Role, x.RankTier });
        });

        modelBuilder.Entity<ScopeMatchCountStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Patch).HasMaxLength(32);
            entity.Property(x => x.QueueFamily).HasMaxLength(64).HasDefaultValue("RANKED_SOLO_DUO");
            entity.Property(x => x.PlatformRegion).HasMaxLength(16);
            entity.Property(x => x.RankScope).HasMaxLength(64);
            entity.HasIndex(x => new { x.Patch, x.QueueFamily, x.PlatformRegion, x.RankScope }).IsUnique();
        });

        modelBuilder.Entity<ChampionBanScopeStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Patch).HasMaxLength(32);
            entity.Property(x => x.QueueFamily).HasMaxLength(64).HasDefaultValue("RANKED_SOLO_DUO");
            entity.Property(x => x.PlatformRegion).HasMaxLength(16);
            entity.Property(x => x.RankScope).HasMaxLength(64);
            // Doubles as the UPSERT target and the point-lookup: a specific (region|"ALL", scope, champion).
            // Its (Patch, PlatformRegion, RankScope) prefix also serves the tier-list all-champions read.
            entity.HasIndex(x => new { x.Patch, x.QueueFamily, x.PlatformRegion, x.RankScope, x.ChampionId }).IsUnique();
        });

        modelBuilder.Entity<ChampionScopeGradeStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Patch).HasMaxLength(32);
            entity.Property(x => x.QueueFamily).HasMaxLength(64).HasDefaultValue("RANKED_SOLO_DUO");
            entity.Property(x => x.PlatformRegion).HasMaxLength(16);
            entity.Property(x => x.RankScope).HasMaxLength(64);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.Property(x => x.PrimaryRole).HasMaxLength(32);
            // UPSERT conflict target + per-champion point lookup (the detail-page hero grade).
            entity.HasIndex(x => new { x.Patch, x.QueueFamily, x.PlatformRegion, x.RankScope, x.Role, x.ChampionId })
                .IsUnique();
            // Tier-list read: every champion in a (region, scope, role) — its prefix also serves the
            // previous-patch movement lookup.
            entity.HasIndex(x => new { x.Patch, x.QueueFamily, x.PlatformRegion, x.RankScope, x.Role });
        });

        modelBuilder.Entity<ChampionMatchupStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Patch).HasMaxLength(32);
            entity.Property(x => x.RankTier).HasMaxLength(32);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.HasIndex(x => new { x.Patch, x.RankTier, x.ChampionId, x.Role, x.OpponentChampionId })
                .IsUnique();
            // Matchup read: one champion+role, rolled up over the tiers in scope.
            entity.HasIndex(x => new { x.Patch, x.ChampionId, x.Role });
        });

        modelBuilder.Entity<ChampionBuildSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Patch).HasMaxLength(32);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.Property(x => x.RankScope).HasMaxLength(64);
            // Doubles as the UPSERT target and the read point-lookup.
            entity.HasIndex(x => new { x.Patch, x.ChampionId, x.Role, x.RankScope }).IsUnique();
        });

        modelBuilder.Entity<BuildResourceSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Patch).HasMaxLength(32);
            entity.Property(x => x.FailureReason).HasMaxLength(512);
            entity.HasIndex(x => new { x.Patch, x.IsActive });
            entity.HasIndex(x => new { x.Patch, x.Status, x.CompletedAtUtc });
        });

        modelBuilder.Entity<BuildResourceStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlatformRegion).HasMaxLength(16);
            entity.Property(x => x.ResourceType).HasMaxLength(16);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.HasIndex(x => new
                { x.SnapshotId, x.PlatformRegion, x.ResourceType, x.ResourceId, x.ChampionId, x.Role })
                .IsUnique();
            entity.HasIndex(x => new { x.SnapshotId, x.ResourceType, x.ResourceId });
            entity.HasOne(x => x.Snapshot)
                .WithMany(x => x.ResourceStats)
                .HasForeignKey(x => x.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BuildResourcePopulationStat>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlatformRegion).HasMaxLength(16);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.HasIndex(x => new { x.SnapshotId, x.PlatformRegion, x.ChampionId, x.Role }).IsUnique();
            entity.HasOne(x => x.Snapshot)
                .WithMany(x => x.PopulationStats)
                .HasForeignKey(x => x.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BuildResourceProcessedMatch>(entity =>
        {
            entity.HasKey(x => new { x.SnapshotId, x.MatchId });
            entity.HasIndex(x => x.MatchId);
            entity.HasOne(x => x.Snapshot)
                .WithMany(x => x.ProcessedMatches)
                .HasForeignKey(x => x.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnalyticsResponseSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Feature).HasMaxLength(64);
            entity.Property(x => x.ScopeKey).HasMaxLength(128);
            entity.Property(x => x.Patch).HasMaxLength(32);
            // Doubles as the UPSERT target and the read point-lookup.
            entity.HasIndex(x => new { x.Feature, x.ScopeKey, x.Patch }).IsUnique();
        });
    }
}
