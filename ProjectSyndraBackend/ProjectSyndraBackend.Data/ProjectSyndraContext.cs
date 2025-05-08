using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data.Models.LoL.Account;
using ProjectSyndraBackend.Data.Models.LoL.Match;
using ProjectSyndraBackend.Data.Models.Service;

namespace ProjectSyndraBackend.Data;

public class ProjectSyndraContext(DbContextOptions<ProjectSyndraContext> options) : DbContext(options)
{
    public DbSet<Summoner> Summoners { get; set; }
    public DbSet<Match> Matches { get; set; }
    public DbSet<MatchDetail> MatchDetails { get; set; }
    public DbSet<MatchSummoner> MatchSummoners { get; set; }
    public DbSet<Runes> Runes { get; set; }
    public DbSet<CurrentDataParameters> CurrentDataParameters { get; set; }
    public DbSet<Rank> Ranks { get; set; }
    public DbSet<HistoricalRank> HistoricalRanks { get; set; }
    public DbSet<CurrentChampionLoadout> CurrentChampionLoadouts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Create unique indexes for external IDs
        modelBuilder.Entity<Match>()
            .HasIndex(m => m.ExternalMatchId)
            .IsUnique();
            
        modelBuilder.Entity<Summoner>()
            .HasIndex(s => s.ExternalSummonerId)
            .IsUnique();
        
        modelBuilder.Entity<Summoner>()
            .HasIndex(s => s.Puuid)
            .IsUnique();
        
        // Set up relationships using GUID primary keys
        modelBuilder.Entity<MatchSummoner>()
            .HasOne(ms => ms.Match)
            .WithMany(m => m.MatchSummoners)
            .HasForeignKey(ms => ms.MatchId);

        modelBuilder.Entity<MatchSummoner>()
            .HasOne(ms => ms.Summoner)
            .WithMany(s => s.MatchSummoners)
            .HasForeignKey(ms => ms.SummonerId);

        modelBuilder.Entity<MatchDetail>()
            .HasOne(md => md.Match)
            .WithMany(m => m.MatchDetails)
            .HasForeignKey(md => md.MatchId);
            
        modelBuilder.Entity<MatchDetail>()
            .HasOne(md => md.Summoner)
            .WithMany()
            .HasForeignKey(md => md.SummonerId);

        modelBuilder.Entity<Rank>()
            .HasOne(r => r.Summoner)
            .WithMany(s => s.Ranks)
            .HasForeignKey(r => r.SummonerId);
    }
}
