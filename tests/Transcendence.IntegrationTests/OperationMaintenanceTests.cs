using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.Service;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class OperationMaintenanceTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task Pending_scan_uses_partial_index_with_realistic_terminal_history()
    {
        await using var db = new TranscendenceContext(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "BackgroundOperations" ("Id", "Kind", "ResourceKey", "PlatformRegion", "GameName", "TagLine",
              "Status", "QueuedAtUtc", "UpdatedAtUtc", "CompletedAtUtc", "PhasesJson", "ResultJson", "DispatchJson", "Revision")
            SELECT gen_random_uuid(), 'summoner_refresh', 'plan-fixture', 'NA1', '', '',
              'succeeded', now(), now(), now(), json_build_object()::text, json_build_object()::text, json_build_object()::text, 0 FROM generate_series(1, 20000);
            ANALYZE "BackgroundOperations";
            """);
        var plan = await db.Database.SqlQueryRaw<string>("""
            EXPLAIN (ANALYZE, BUFFERS) SELECT "Id" FROM "BackgroundOperations"
            WHERE "DispatchedAtUtc" IS NULL AND "Status" NOT IN ('succeeded', 'partial', 'failed')
            ORDER BY "QueuedAtUtc" LIMIT 100
            """).ToListAsync();
        string.Join('\n', plan).Should().Contain("IX_BackgroundOperations_PendingDispatch");
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Retention_is_bounded_preserves_active_children_and_removes_owned_requests()
    {
        await using var db = new TranscendenceContext(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString).Options);
        var now = DateTime.UtcNow;
        BackgroundOperation Row(string status) => new()
        {
            Id = Guid.NewGuid(), Kind = "summoner_refresh", ResourceKey = Guid.NewGuid().ToString(),
            Status = status, CompletedAtUtc = status == "running" ? null : now.AddDays(-10),
            QueuedAtUtc = now.AddDays(-11), UpdatedAtUtc = now.AddDays(-10)
        };
        var parent = Row("succeeded");
        var active = Row("running"); active.ParentId = parent.Id;
        var old = Enumerable.Range(0, 3).Select(_ => Row("failed")).ToArray();
        db.AddRange(parent, active); db.AddRange(old);
        var request = new BackgroundOperationRequest { Id = Guid.NewGuid(), ExecutionId = old[0].Id, OwnerKind = "User", OwnerId = Guid.NewGuid() };
        db.Add(request); await db.SaveChangesAsync();
        using var telemetry = new OperationTelemetry();
        var maintenance = new OperationMaintenance(db, Options.Create(new OperationRetentionOptions { BatchSize = 1 }), telemetry);
        var before = await db.BackgroundOperations.CountAsync();
        await maintenance.RunBatchAsync(now);
        (await db.BackgroundOperations.CountAsync()).Should().Be(before - 1);
        (await db.BackgroundOperations.AnyAsync(x => x.Id == active.Id)).Should().BeTrue();
        (await db.BackgroundOperations.AnyAsync(x => x.Id == parent.Id)).Should().BeTrue();
        // Subsequent batches drain all old leaves while keeping the parent/active child pair.
        for (var i = 0; i < 4; i++) await maintenance.RunBatchAsync(now);
        (await db.BackgroundOperations.CountAsync(x => old.Select(r => r.Id).Contains(x.Id))).Should().Be(0);
        (await db.BackgroundOperationRequests.AnyAsync(x => x.Id == request.Id)).Should().BeFalse();
        var index = await db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes
            WHERE indexname = 'IX_BackgroundOperations_PendingDispatch'
            """).SingleAsync();
        index.Should().Contain("WHERE").And.Contain("DispatchedAtUtc").And.Contain("QueuedAtUtc");
    }
}
