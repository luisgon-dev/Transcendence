using Microsoft.EntityFrameworkCore;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rank>()
            .HasIndex(x => new { x.SummonerId, x.QueueType })
            .IsUnique();

        modelBuilder.Entity<Match>()
            .HasIndex(x => new { x.MatchId })
            .IsUnique();

        // Helpful secondary indexes for query patterns on matches
        modelBuilder.Entity<Match>()
            .HasIndex(x => x.MatchDate);
        modelBuilder.Entity<Match>()
            .HasIndex(x => x.QueueType);

        // Summoner lookups by Puuid
        modelBuilder.Entity<Summoner>()
            .HasIndex(s => s.Puuid);

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
            entity.HasIndex(p => new { p.MatchId, p.SummonerId })
                .IsUnique();

            // Common filter/index fields
            entity.HasIndex(p => p.SummonerId);
            entity.HasIndex(p => p.ChampionId);
            entity.HasIndex(p => new { p.ChampionId, p.TeamPosition });
            entity.HasIndex(p => p.MatchId);
        });

        // Versioned static data configuration
        modelBuilder.Entity<Patch>(entity =>
        {
            entity.HasKey(p => p.Version);
        });

        // RefreshLock configuration
        modelBuilder.Entity<RefreshLock>(entity =>
        {
            entity.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<RuneVersion>(entity =>
        {
            entity.HasKey(rv => new { rv.RuneId, rv.PatchVersion });

            entity.HasOne(rv => rv.Patch)
                .WithMany()
                .HasForeignKey(rv => rv.PatchVersion);
        });

        modelBuilder.Entity<ItemVersion>(entity =>
        {
            entity.HasKey(iv => new { iv.ItemId, iv.PatchVersion });

            entity.HasOne(iv => iv.Patch)
                .WithMany()
                .HasForeignKey(iv => iv.PatchVersion);
        });

        // Match participant join tables configuration
        modelBuilder.Entity<MatchParticipantRune>(entity =>
        {
            entity.HasKey(mpr => new { mpr.MatchParticipantId, mpr.RuneId });

            entity.HasOne(mpr => mpr.MatchParticipant)
                .WithMany(mp => mp.Runes)
                .HasForeignKey(mpr => mpr.MatchParticipantId);

            entity.HasOne(mpr => mpr.RuneVersion)
                .WithMany()
                .HasForeignKey(mpr => new { mpr.RuneId, mpr.PatchVersion });
        });

        modelBuilder.Entity<MatchParticipantItem>(entity =>
        {
            entity.HasKey(mpi => new { mpi.MatchParticipantId, mpi.ItemId });

            entity.HasOne(mpi => mpi.MatchParticipant)
                .WithMany(mp => mp.Items)
                .HasForeignKey(mpi => mpi.MatchParticipantId);

            entity.HasOne(mpi => mpi.ItemVersion)
                .WithMany()
                .HasForeignKey(mpi => new { mpi.ItemId, mpi.PatchVersion });
        });
    }
}