using FluentAssertions;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Tests;

public class BuildLabEvidenceGateTests
{
    private static readonly BuildLabModelingOptions Options = new();

    [Fact]
    public void Evaluate_PublishesOnlyWhenEveryInitialGatePasses()
    {
        var estimate = PassingEstimate();

        BuildLabEvidenceGate.Evaluate(estimate, Options, out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData("observed")]
    [InlineData("ess")]
    [InlineData("interval")]
    [InlineData("overlap")]
    [InlineData("balance")]
    [InlineData("stability")]
    [InlineData("estimate")]
    public void Evaluate_FailsEachEvidenceDimensionIndependently(string failingGate)
    {
        var estimate = PassingEstimate();
        switch (failingGate)
        {
            case "observed": estimate.ObservedCount = 999; break;
            case "ess": estimate.EffectiveSampleSize = 499; break;
            case "interval": estimate.ConfidenceHigh = 0.031; break;
            case "overlap": estimate.PropensityOverlap = 0.899; break;
            case "balance": estimate.CovariateBalance = 0.101; break;
            case "stability": estimate.StableAcrossFolds = false; break;
            case "estimate": estimate.AdjustedWpa = null; break;
        }

        BuildLabEvidenceGate.Evaluate(estimate, Options, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void UsesV1OrStricterThresholds_RejectsAnOperationalLowering()
    {
        var lowered = new BuildLabModelingOptions { MinimumObservedActions = 999 };

        BuildLabEvidenceGate.UsesV1OrStricterThresholds(lowered).Should().BeFalse();
        BuildLabEvidenceGate.UsesV1OrStricterThresholds(new BuildLabModelingOptions()).Should().BeTrue();
    }

    [Fact]
    public void RegionalOverrideIsMeaningful_RequiresCorrectedSeparationFromGlobal()
    {
        var global = PassingEstimate();
        global.AdjustedWpa = 0.01;
        global.ConfidenceLow = 0.005;
        global.ConfidenceHigh = 0.015;
        var regional = PassingEstimate();
        regional.AdjustedWpa = 0.012;
        regional.ConfidenceLow = 0.007;
        regional.ConfidenceHigh = 0.017;

        BuildLabEvidenceGate.RegionalOverrideIsMeaningful(regional, global, 20).Should().BeFalse();

        regional.AdjustedWpa = 0.08;
        regional.ConfidenceLow = 0.075;
        regional.ConfidenceHigh = 0.085;
        BuildLabEvidenceGate.RegionalOverrideIsMeaningful(regional, global, 20).Should().BeTrue();
    }

    private static AdjustedActionEstimate PassingEstimate() => new()
    {
        Generation = null!,
        ObservedCount = 1000,
        EffectiveSampleSize = 500,
        ConfidenceLow = 0,
        ConfidenceHigh = 0.03,
        PropensityOverlap = 0.90,
        CovariateBalance = 0.10,
        StableAcrossFolds = true,
        AdjustedWpa = 0.01
    };
}
