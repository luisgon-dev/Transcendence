using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Account;
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
    public DbSet<CurrentChampionLoadout> CurrentChampionLoadouts { get; set; }
    public DbSet<MatchParticipant> MatchParticipants { get; set; }

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
    public DbSet<UserRefreshToken> UserRefreshTokens { get; set; }
    public DbSet<UserFavoriteSummoner> UserFavoriteSummoners { get; set; }
    public DbSet<UserPreferences> UserPreferences { get; set; }
    public DbSet<LiveGameSnapshot> LiveGameSnapshots { get; set; }
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

        // Summoner lookups by Puuid
        modelBuilder.Entity<Summoner>()
            .HasIndex(s => s.Puuid);

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
            entity.HasIndex(p => p.ChampionId);
            entity.HasIndex(p => new
            {
                p.ChampionId,
                p.TeamPosition
            });
            entity.HasIndex(p => p.MatchId);
        });

        // Versioned static data configuration
        modelBuilder.Entity<Patch>(entity => { entity.HasKey(p => p.Version); });

        // RefreshLock configuration
        modelBuilder.Entity<RefreshLock>(entity => { entity.HasIndex(x => x.Key).IsUnique(); });

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

        modelBuilder.Entity<LiveGameSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Puuid, x.PlatformRegion, x.ObservedAtUtc });
            entity.HasIndex(x => x.NextPollAtUtc);
            entity.Property(x => x.State).IsRequired();
            entity.Property(x => x.PlatformRegion).IsRequired();
            entity.Property(x => x.Puuid).IsRequired();
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

            entity.HasOne(mpr => mpr.RuneVersion)
                .WithMany()
                .HasForeignKey(mpr => new
                {
                    mpr.RuneId,
                    mpr.PatchVersion
                });

            entity.HasIndex(mpr => new { mpr.RuneId, mpr.PatchVersion });
        });

        modelBuilder.Entity<MatchParticipantRune>()
            .HasQueryFilter(mpr => mpr.MatchParticipant.Match.Status != FetchStatus.PermanentlyUnfetchable);

        modelBuilder.Entity<MatchParticipantItem>(entity =>
        {
            entity.HasKey(mpi => new
            {
                mpi.MatchParticipantId,
                mpi.ItemId
            });

            entity.HasOne(mpi => mpi.MatchParticipant)
                .WithMany(mp => mp.Items)
                .HasForeignKey(mpi => mpi.MatchParticipantId);

            entity.HasOne(mpi => mpi.ItemVersion)
                .WithMany()
                .HasForeignKey(mpi => new
                {
                    mpi.ItemId,
                    mpi.PatchVersion
                });
        });

        // Match participant item/rune filters align with match/participant filters
        modelBuilder.Entity<MatchParticipantItem>()
            .HasQueryFilter(mpi => mpi.MatchParticipant.Match.Status != FetchStatus.PermanentlyUnfetchable);
    }
}



