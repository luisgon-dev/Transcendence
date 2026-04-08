using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public class AnalyticsControllerTests
{
    [Fact]
    public async Task GetStatus_ReturnsActivePatchMetadata()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        db.Patches.Add(new Patch
        {
            Version = "16.6",
            ReleaseDate = now.AddHours(-6),
            DetectedAt = now.AddHours(-5),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = new AnalyticsController(
            Mock.Of<IChampionAnalyticsService>(),
            db,
            Options.Create(new MultiRegionIngestionOptions()));

        var result = await controller.GetStatus(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<AnalyticsPatchStatusDto>().Subject;
        payload.Patch.Should().Be("16.6");
        payload.ActivePatchReleasedAtUtc.Should().NotBeNull();
        payload.ActivePatchDetectedAtUtc.Should().NotBeNull();
    }

    private static TranscendenceContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TranscendenceContext(options);
    }
}
