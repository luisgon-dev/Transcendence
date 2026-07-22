using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.StaticData.Implementations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class StaticDataServiceTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public async Task EnsureStaticDataForPatchAsync_RemovesOnlyNonAuthoritativeCacheEntries(
        bool shouldCache,
        int expectedRemoveCalls)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Patches.Add(new Patch
        {
            Version = "16.14",
            ReleaseDate = DateTime.UtcNow,
            DetectedAt = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string[]?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(shouldCache);

        var service = new StaticDataService(
            db,
            Mock.Of<IHttpClientFactory>(),
            cache.Object,
            Options.Create(new PatchPromotionOptions()),
            NullLogger<StaticDataService>.Instance);

        await service.EnsureStaticDataForPatchAsync("16.14");

        cache.Verify(
            value => value.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedRemoveCalls));
    }
}
