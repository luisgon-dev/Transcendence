using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;

namespace Transcendence.Service.Core.Tests.Support;

internal sealed class SqliteCompatibleTranscendenceContext(DbContextOptions<TranscendenceContext> options)
    : TranscendenceContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostgreSQL array default SQL is not valid in SQLite-backed tests.
        modelBuilder.Entity<ItemVersion>()
            .Property(x => x.BuildsFrom)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<ItemVersion>()
            .Property(x => x.BuildsInto)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<ChampionVersion>()
            .Property(x => x.Roles)
            .HasDefaultValueSql("'[]'");
    }
}
