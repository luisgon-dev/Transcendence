using FluentAssertions;
using Transcendence.Service.Core.Services.StaticData.Models;

namespace Transcendence.Service.Core.Tests;

public class RunePathIdsTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(8000, false)]
    [InlineData(RunePathIds.StatMods, false)]
    [InlineData(4999, true)]
    public void IsRealRunePath_RecognizesOnlyRiotStyleIds(int pathId, bool expected)
    {
        RunePathIds.IsRealRunePath(pathId).Should().Be(expected);
    }

    [Theory]
    [InlineData(4999, false)]
    [InlineData(RunePathIds.StatMods, true)]
    [InlineData(5001, true)]
    public void IsStatModPath_RecognizesSyntheticStatPaths(int pathId, bool expected)
    {
        RunePathIds.IsStatModPath(pathId).Should().Be(expected);
    }
}
