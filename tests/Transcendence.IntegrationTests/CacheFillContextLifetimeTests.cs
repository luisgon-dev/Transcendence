using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Cache;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Services fill HybridCache entries with queries on the request's scoped DbContext. Plain HybridCache
/// returns to a cancelled caller at once while the fill is still running on that context, so a client
/// that gave up at the proxy's 10s limit left the request disposing its context -- or returning the
/// connection to the pool -- under a live command. On prod that surfaced as ObjectDisposedException,
/// "Index was out of range" and OutOfMemoryException from a desynchronized Npgsql connection. The
/// hosts register <see cref="FillToCompletionHybridCache"/>, under which the caller waits for the fill.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class CacheFillContextLifetimeTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task PlainHybridCache_ReturnsToACancelledCallerWhileTheFillStillUsesItsContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        var cache = services.BuildServiceProvider().GetRequiredService<HybridCache>();

        var (callerFinishedFirst, _) = await CancelMidFillAsync(cache);

        callerFinishedFirst.Should().BeTrue("this is the lifetime bug the host registration exists to prevent");
    }

    [Fact]
    public async Task TheWebApisCache_KeepsACancelledCallerUntilTheFillIsDone_AndCachesTheResult()
    {
        var cache = fixture.Factory.Services.GetRequiredService<HybridCache>();
        cache.Should().BeOfType<FillToCompletionHybridCache>();

        var (callerFinishedFirst, value) = await CancelMidFillAsync(cache);

        callerFinishedFirst.Should().BeFalse("the caller must not get its context back while the fill runs on it");
        value.Should().Be(1);
    }

    // Starts a fill whose query (on the caller's context) outlives the caller's token, and reports
    // whether the caller got control back before the fill finished with the context.
    private async Task<(bool CallerFinishedFirst, int? Value)> CancelMidFillAsync(HybridCache cache)
    {
        await using var context = new TranscendenceContext(
            new DbContextOptionsBuilder<TranscendenceContext>().UseNpgsql(fixture.ConnectionString).Options);
        var fillDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        int? value = null;
        try
        {
            value = await cache.GetOrCreateAsync($"lifetime-{Guid.NewGuid():N}", async _ =>
            {
                await context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(1)");
                fillDone.SetResult();
                return 1;
            }, cancellationToken: caller.Token);
        }
        catch (OperationCanceledException)
        {
        }

        var callerFinishedFirst = !fillDone.Task.IsCompleted;
        await fillDone.Task;
        return (callerFinishedFirst, value);
    }
}
