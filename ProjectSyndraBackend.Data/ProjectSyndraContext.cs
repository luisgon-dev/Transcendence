using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data.Models.Account;
using ProjectSyndraBackend.Data.Models.Match;

namespace ProjectSyndraBackend.Data;

public class ProjectSyndraContext : DbContext
{
    public DbSet<Summoner> Summoners { get; set; }
    public DbSet<Match> Matches { get; set; }
    public DbSet<MatchDetail> MatchDetails { get; set; }
    public DbSet<MatchSummoner> MatchSummoners { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchSummoner>()
            .HasKey(ms => new { ms.MatchId, ms.SummonerId });
        
        modelBuilder.Entity<MatchSummoner>()
            .HasOne(ms => ms.Match)
            .WithMany(m => m.MatchSummoners)
            .HasForeignKey(ms => ms.MatchId);
        
        modelBuilder.Entity<MatchSummoner>()
            .HasOne(ms => ms.Summoner)
            .WithMany(s => s.MatchSummoners)
            .HasForeignKey(ms => ms.SummonerId);
    }
    
}