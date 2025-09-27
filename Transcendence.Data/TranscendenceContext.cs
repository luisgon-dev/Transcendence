using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.Service;

namespace Transcendence.Data;

public class TranscendenceContext(DbContextOptions<TranscendenceContext> options) : DbContext(options)
{
    public DbSet<Summoner> Summoners { get; set; }
    public DbSet<Match> Matches { get; set; }
    public DbSet<MatchSummoner> MatchSummoners { get; set; }
    public DbSet<Runes> Runes { get; set; }
    public DbSet<CurrentDataParameters> CurrentDataParameters { get; set; }
    public DbSet<Rank> Ranks { get; set; }
    public DbSet<HistoricalRank> HistoricalRanks { get; set; }
    public DbSet<CurrentChampionLoadout> CurrentChampionLoadouts { get; set; }
    public DbSet<MatchParticipant> MatchParticipants { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Summoner>()
            .HasMany(m => m.Matches)
            .WithMany(e => e.Summoners)
            .UsingEntity<MatchSummoner>();


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

        modelBuilder.Entity<Runes>()
            .HasIndex(r => new
            {
                r.PrimaryStyle,
                r.SubStyle,
                r.Perk0,
                r.Perk1,
                r.Perk2,
                r.Perk3,
                r.Perk4,
                r.Perk5,
                r.StatDefense,
                r.StatFlex,
                r.StatOffense
            })
            .HasDatabaseName("IX_Runes_Combination");
    }
}