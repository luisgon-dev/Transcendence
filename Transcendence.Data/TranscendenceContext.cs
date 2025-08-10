using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.Service;

namespace Transcendence.Data;

public class TranscendenceContext(DbContextOptions<TranscendenceContext> options) : DbContext(options)
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


        modelBuilder.Entity<Rank>()
            .HasIndex(x => new { x.SummonerId, x.QueueType })
            .IsUnique();
        
        modelBuilder.Entity<Match>()
            .HasIndex(x => new { x.MatchId })
            .IsUnique();
        
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