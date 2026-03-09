using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Models.Tft.Match;
using Transcendence.Data.Models.Tft.Static;

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

        modelBuilder.Entity<TftAugmentVersion>()
            .Property(x => x.AssociatedTraits)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<TftAugmentVersion>()
            .Property(x => x.IncompatibleTraits)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<TftAugmentVersion>()
            .Property(x => x.Tags)
            .HasDefaultValueSql("'[]'");

        modelBuilder.Entity<TftItemVersion>()
            .Property(x => x.AssociatedTraits)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<TftItemVersion>()
            .Property(x => x.IncompatibleTraits)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<TftItemVersion>()
            .Property(x => x.Composition)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<TftItemVersion>()
            .Property(x => x.Tags)
            .HasDefaultValueSql("'[]'");

        modelBuilder.Entity<TftUnitVersion>()
            .Property(x => x.Traits)
            .HasDefaultValueSql("'[]'");

        modelBuilder.Entity<TftMatchParticipant>()
            .Property(x => x.Augments)
            .HasDefaultValueSql("'[]'");

        modelBuilder.Entity<TftMatchParticipantUnit>()
            .Property(x => x.Items)
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<TftMatchParticipantUnit>()
            .Property(x => x.ItemNames)
            .HasDefaultValueSql("'[]'");
    }
}
