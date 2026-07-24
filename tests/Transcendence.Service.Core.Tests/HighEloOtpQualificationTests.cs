using FluentAssertions;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Tests;

public sealed class HighEloOtpQualificationTests
{
    [Fact]
    public void RequiresAFiftyGameSample()
    {
        var result = AddOrUpdateHighEloProfiles.EvaluateOtp(Enumerable.Repeat(145, 49).ToList());

        result.IsQualified.Should().BeFalse();
        result.SampleSize.Should().Be(49);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(40, true)]
    public void RequiresThirtyGamesOnOneChampion(int championGames, bool expected)
    {
        var sample = Enumerable.Repeat(145, championGames)
            .Concat(Enumerable.Range(1, 50 - championGames))
            .ToList();

        var result = AddOrUpdateHighEloProfiles.EvaluateOtp(sample);

        result.IsQualified.Should().Be(expected);
        result.ChampionId.Should().Be(145);
        result.ChampionGames.Should().Be(championGames);
        result.SampleSize.Should().Be(50);
    }
}
