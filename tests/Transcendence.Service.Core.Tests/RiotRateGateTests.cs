using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Xunit;

namespace Transcendence.Service.Core.Tests;

public class RiotRateGateTests
{
    // No refill during a sub-second test (period far in the future), so "exhausted" is deterministic.
    private static RiotRateGate Build(int burst, int maxWaitSeconds = 1) =>
        new(Options.Create(new RiotRateGateOptions
        {
            Enabled = true,
            BurstTokens = burst,
            TokensPerPeriod = 1,
            ReplenishPeriodSeconds = 3600,
            MaxWaitSeconds = maxWaitSeconds
        }));

    [Fact]
    public async Task Disabled_AlwaysAllows()
    {
        using var gate = new RiotRateGate(Options.Create(new RiotRateGateOptions { Enabled = false }));
        Assert.True(await gate.AcquireAsync("AMERICAS"));
        Assert.True(await gate.AcquireAsync("AMERICAS"));
    }

    [Fact]
    public async Task AllowsUpToBurst_ThenRejectsWhenExhausted()
    {
        using var gate = Build(burst: 2);
        Assert.True(await gate.AcquireAsync("EUROPE"));
        Assert.True(await gate.AcquireAsync("EUROPE"));
        // Bucket empty and no refill within the max wait -> reject so the caller skips, not blocks forever.
        Assert.False(await gate.AcquireAsync("EUROPE"));
    }

    [Fact]
    public async Task PartitionsAreIndependentPerRegion()
    {
        using var gate = Build(burst: 1);
        Assert.True(await gate.AcquireAsync("ASIA")); // consumes ASIA's only token
        Assert.True(await gate.AcquireAsync("SEA"));  // SEA has its own independent token
        Assert.False(await gate.AcquireAsync("ASIA")); // ASIA now exhausted
    }

    [Fact]
    public async Task Pause_DrainsOnlyTheAffectedRegionUntilRetryAfterExpires()
    {
        using var gate = Build(burst: 2);

        gate.Pause("AMERICAS", TimeSpan.FromMinutes(1));

        Assert.False(await gate.AcquireAsync("AMERICAS"));
        Assert.True(await gate.AcquireAsync("EUROPE"));
    }

    [Fact]
    public async Task NullOrEmptyRoutingKey_DoesNotThrow()
    {
        using var gate = Build(burst: 2);
        Assert.True(await gate.AcquireAsync(""));
        Assert.True(await gate.AcquireAsync("  ")); // both normalize to the same "default" partition (burst 2)
    }
}
