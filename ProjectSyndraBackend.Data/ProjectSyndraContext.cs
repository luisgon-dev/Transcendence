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
        modelBuilder.Entity<Summoner>()
            .HasMany(m => m.Matches)
            .WithMany(e => e.Summoners)
            .UsingEntity<MatchSummoner>();
    }
}