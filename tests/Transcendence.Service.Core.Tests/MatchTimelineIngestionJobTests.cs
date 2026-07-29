using FluentAssertions;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Tests;

public class MatchTimelineIngestionJobTests
{
    [Fact]
    public void BuildMinuteMarks_CoversCadenceUpToGameLengthPlusAnchor()
    {
        // 30-minute game, every-2-min cadence, anchor 15.
        var marks = MatchTimelineIngestionJob.BuildMinuteMarks(1800, 2, 15);

        marks.Should().BeInAscendingOrder();
        marks.Should().OnlyHaveUniqueItems();
        marks.First().Should().Be(2);
        marks.Last().Should().Be(30);
        marks.Should().Contain(15); // analytics anchor always present
    }

    [Fact]
    public void BuildMinuteMarks_ShortGameStillIncludesAnchorAndStopsAtGameLength()
    {
        // 11-minute game -> cadence {2,4,6,8,10} (12 > 11), plus the anchor 15.
        var marks = MatchTimelineIngestionJob.BuildMinuteMarks(11 * 60, 2, 15);

        marks.Should().Contain(10);
        marks.Should().Contain(15); // anchor kept even though the game is shorter
        marks.Should().NotContain(12); // never sample past game length (besides the anchor)
    }

    [Fact]
    public void BuildMinuteMarks_OneMinuteModelCadencePreservesEveryMinute()
    {
        var marks = MatchTimelineIngestionJob.BuildMinuteMarks(20 * 60, 1, 15);

        marks.Should().Equal(Enumerable.Range(1, 20));
    }
}
