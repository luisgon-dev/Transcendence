using FluentAssertions;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.StaticData.Models;

namespace Transcendence.Service.Core.Tests;

public class RuneSelectionMapperTests
{
    [Fact]
    public void Map_UsesExplicitHierarchyAndRejectsAStatPathAsAStyle()
    {
        var selections = new List<StoredRuneSelection>
        {
            new(8005, RuneSelectionTree.Primary, 0, 0),
            new(8473, RuneSelectionTree.Secondary, 0, 8400),
            new(5008, RuneSelectionTree.StatShards, 0, 0)
        };
        var metadata = new Dictionary<int, RuneSelectionMetadata>
        {
            [8005] = new(RunePathIds.StatMods, 0),
            [8473] = new(8400, 1),
            [5008] = new(RunePathIds.StatMods, 0)
        };

        var result = RuneSelectionMapper.Map(
            selections,
            runeId => metadata.TryGetValue(runeId, out var value) ? value : null);

        result.PrimaryStyleId.Should().Be(0);
        result.SubStyleId.Should().Be(8400);
        result.PrimaryRunes.Should().Equal(8005);
        result.SubRunes.Should().Equal(8473);
        result.StatShards.Should().Equal(5008);
    }

    [Fact]
    public void Map_InfersLegacyTreesAndLimitsStatShards()
    {
        var selections = new List<StoredRuneSelection>
        {
            new(5001, RuneSelectionTree.Primary, 3, 0),
            new(9111, RuneSelectionTree.Primary, 2, 0),
            new(8473, RuneSelectionTree.Primary, 1, 0),
            new(8005, RuneSelectionTree.Primary, 0, 0),
            new(5002, RuneSelectionTree.Primary, 4, 0),
            new(5003, RuneSelectionTree.Primary, 5, 0),
            new(5010, RuneSelectionTree.Primary, 6, 0)
        };
        var metadata = new Dictionary<int, RuneSelectionMetadata>
        {
            [8005] = new(8000, 0),
            [9111] = new(8000, 1),
            [8473] = new(8400, 1),
            [5001] = new(RunePathIds.StatMods, 0),
            [5002] = new(RunePathIds.StatMods, 1),
            [5003] = new(RunePathIds.StatMods, 2),
            [5010] = new(RunePathIds.StatMods, 3)
        };

        var result = RuneSelectionMapper.Map(
            selections,
            runeId => metadata.TryGetValue(runeId, out var value) ? value : null);

        result.PrimaryStyleId.Should().Be(8000);
        result.SubStyleId.Should().Be(8400);
        result.PrimaryRunes.Should().Equal(8005, 9111);
        result.SubRunes.Should().Equal(8473);
        result.StatShards.Should().Equal(5001, 5002, 5003);
    }

    [Fact]
    public void Map_TreatsDuplicateStructuredSlotsAsLegacyData()
    {
        var selections = new List<StoredRuneSelection>
        {
            new(8005, RuneSelectionTree.Primary, 0, 8000),
            new(8473, RuneSelectionTree.Primary, 0, 8000)
        };
        var metadata = new Dictionary<int, RuneSelectionMetadata>
        {
            [8005] = new(8000, 0),
            [8473] = new(8400, 1)
        };

        var result = RuneSelectionMapper.Map(
            selections,
            runeId => metadata.TryGetValue(runeId, out var value) ? value : null);

        result.PrimaryStyleId.Should().Be(8000);
        result.SubStyleId.Should().Be(8400);
    }
}
