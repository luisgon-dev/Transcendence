using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.IntegrationTests;

/// <summary>
/// The matchup generation's rank-snapshot write, against REAL Postgres.
///
/// This is the defect these cover: <c>EnsureRankSnapshotAsync</c> read the summoner ids already
/// snapshotted, diffed them against the cohort, and inserted the remainder with EF. Nothing serialises
/// two runs, and a slow database produces them routinely — the job's reads time out, Hangfire retries
/// it, and the retry overlaps the original. Both then read the same "existing" set before either
/// inserts, and the loser died on PK_ChampionMatchupRankSnapshots (23505), failing the whole
/// generation. Prod showed roughly one such error an hour, and six unbroken hours of them while the
/// Build Lab modeler saturated the same database.
///
/// It has to be a Postgres test: the fix is an ON CONFLICT DO NOTHING insert fed by unnest, and the
/// SQLite-backed unit suite can model neither.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class MatchupRankSnapshotConcurrencyTests(PostgresIntegrationFixture fixture)
{
    private const int Summoners = 400;

    private TranscendenceContext NewDb() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    [Fact]
    public async Task ConcurrentRankSnapshotPasses_AbsorbTheCollisionInsteadOfFailingTheGeneration()
    {
        var (snapshotId, summonerIds) = await SeedCohortAsync();

        // Four racers over one empty snapshot: every one of them reads an empty "existing" set and
        // then inserts the whole cohort, which is exactly the interleaving that used to throw. Before
        // the ON CONFLICT insert this failed with a DbUpdateException wrapping 23505.
        var passes = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = NewDb();
            var snapshot = await db.ChampionMatchupSnapshots.SingleAsync(x => x.Id == snapshotId);
            await RefresherFor(db).EnsureRankSnapshotAsync(snapshot, CancellationToken.None);
        });

        var act = () => Task.WhenAll(passes);

        await act.Should().NotThrowAsync();

        await using var verification = NewDb();
        var persisted = await verification.ChampionMatchupRankSnapshots
            .AsNoTracking()
            .Where(row => row.SnapshotId == snapshotId)
            .ToListAsync();
        // One row per summoner: the losers were discarded, not duplicated, and nobody was dropped.
        persisted.Should().HaveCount(Summoners);
        persisted.Select(row => row.SummonerId).Should().BeEquivalentTo(summonerIds);
        // No Rank rows were seeded, so every summoner attributes to the unranked bucket. This asserts
        // the unnest'd tier column stays aligned with its summoner id rather than shifting.
        persisted.Should().OnlyContain(row => row.RankTier == RankTierCatalog.Unranked);
    }

    [Fact]
    public async Task ARepeatedPass_IsANoOpRatherThanASecondInsert()
    {
        var (snapshotId, _) = await SeedCohortAsync();

        for (var pass = 0; pass < 2; pass++)
        {
            await using var db = NewDb();
            var snapshot = await db.ChampionMatchupSnapshots.SingleAsync(x => x.Id == snapshotId);
            await RefresherFor(db).EnsureRankSnapshotAsync(snapshot, CancellationToken.None);
        }

        await using var verification = NewDb();
        var count = await verification.ChampionMatchupRankSnapshots
            .CountAsync(row => row.SnapshotId == snapshotId);
        count.Should().Be(Summoners, "a resumed generation must not re-insert what it already wrote");
    }

    private static PrecomputedAnalyticsRefresher RefresherFor(TranscendenceContext db) =>
        new(db,
            new ChampionBuildComputeService(db, Options.Create(new ChampionAnalyticsComputeOptions()),
                NullLogger<ChampionBuildComputeService>.Instance),
            new ChampionProComputeService(db, Options.Create(new ChampionAnalyticsComputeOptions())),
            Options.Create(new TieringOptions()),
            NullLogger<PrecomputedAnalyticsRefresher>.Instance);

    /// <summary>
    /// A Building snapshot plus one fact per summoner. Facts carry no foreign key to summoners or
    /// matches, and no Rank rows are seeded, so this is the whole fixture the rank pass needs.
    /// </summary>
    private async Task<(Guid SnapshotId, List<Guid> SummonerIds)> SeedCohortAsync()
    {
        // Unique per test: the collection shares one container and these tests do not tear down.
        var patch = $"rank-{Guid.NewGuid():N}"[..12];
        var now = DateTime.UtcNow;
        var summonerIds = Enumerable.Range(0, Summoners).Select(_ => Guid.NewGuid()).ToList();

        await using var db = NewDb();
        var snapshot = new ChampionMatchupSnapshot
        {
            Id = Guid.NewGuid(),
            Patch = patch,
            Status = ChampionMatchupSnapshotStatus.Building,
            StartedAtUtc = now,
            SourceCutoffUtc = now
        };
        db.ChampionMatchupSnapshots.Add(snapshot);
        db.ChampionMatchupFacts.AddRange(summonerIds.Select((summonerId, index) => new ChampionMatchupFact
        {
            Id = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            ChampionParticipantId = 1,
            SummonerId = summonerId,
            Patch = patch,
            ChampionId = 100 + (index % 5),
            Role = "TOP",
            OpponentChampionId = 200 + (index % 5),
            Win = index % 2 == 0,
            CreatedAtUtc = now,
            // The rank pass selects facts at or before the snapshot's cutoff.
            UpdatedAtUtc = now.AddMinutes(-1)
        }));
        await db.SaveChangesAsync();

        return (snapshot.Id, summonerIds);
    }
}
