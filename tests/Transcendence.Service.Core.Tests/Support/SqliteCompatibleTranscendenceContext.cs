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
            .PrimitiveCollection(x => x.BuildsFrom)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<ItemVersion>()
            .PrimitiveCollection(x => x.BuildsInto)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<ChampionVersion>()
            .PrimitiveCollection(x => x.Roles)
            .HasDefaultValueSql("'[]'");
    }
}
