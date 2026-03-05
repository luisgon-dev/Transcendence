using System.Security.Claims;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public class ProSummonersControllerTests
{
    [Fact]
    public async Task Refresh_WhenLockHeld_ReturnsRetryHintWithPollLinkAndSkipsEnqueue()
    {
        await using var db = CreateDbContext();
        var tracked = SeedTrackedSummoner(db);
        await db.SaveChangesAsync();

        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        refreshLockRepository
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        refreshLockRepository
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Data.Models.Service.RefreshLock
            {
                Key = "summoner-refresh:NA1:NAME:TAG",
                LockedUntilUtc = DateTime.UtcNow.AddSeconds(40)
            });

        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        var controller = BuildController(
            db,
            refreshLockRepository.Object,
            backgroundJobClient.Object);
        controller.Url = new StaticUrlHelper("https://localhost/api/admin/pro-summoners/tracked-id");

        var result = await controller.Refresh(tracked.Id, CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<SummonerAcceptedResponse>().Subject;
        payload.Message.Should().Be("Refresh in process");
        payload.Poll.Should().Be("https://localhost/api/admin/pro-summoners/tracked-id");
        payload.RetryAfterSeconds.Should().BeGreaterThan(0);
        backgroundJobClient.Verify(
            x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()),
            Times.Never);
    }

    [Fact]
    public async Task Refresh_WhenPriorityLockUnavailable_QueuesMainRefreshAndReturnsAcceptedPayload()
    {
        await using var db = CreateDbContext();
        var tracked = SeedTrackedSummoner(db);
        await db.SaveChangesAsync();

        var expectedMainKey = RefreshLockKeys.BuildSummonerRefreshKey(Camille.Enums.PlatformRoute.NA1, tracked.GameName!,
            tracked.TagLine!);
        var expectedPriorityKey = RefreshLockKeys.BuildApiPriorityKey(Camille.Enums.PlatformRoute.NA1, tracked.GameName!,
            tracked.TagLine!);

        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        refreshLockRepository
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<string, TimeSpan, CancellationToken>((key, _, _) => Task.FromResult(key == expectedMainKey));

        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        backgroundJobClient
            .Setup(x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()))
            .Returns("job-1");

        var auditService = new Mock<IAdminAuditService>();
        auditService
            .Setup(x => x.WriteAsync(It.IsAny<AdminAuditWriteRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = BuildController(
            db,
            refreshLockRepository.Object,
            backgroundJobClient.Object,
            auditService.Object);
        controller.Url = new StaticUrlHelper("https://localhost/api/admin/pro-summoners/tracked-id");

        var result = await controller.Refresh(tracked.Id, CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<SummonerAcceptedResponse>().Subject;
        payload.Message.Should().Be("Refresh queued");
        payload.Poll.Should().Be("https://localhost/api/admin/pro-summoners/tracked-id");
        payload.RetryAfterSeconds.Should().BeNull();

        refreshLockRepository.Verify(
            x => x.TryAcquireAsync(expectedMainKey, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
        refreshLockRepository.Verify(
            x => x.TryAcquireAsync(expectedPriorityKey, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
        backgroundJobClient.Verify(
            x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()),
            Times.Once);
        auditService.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(r => r.Action == "pro-summoners.refresh"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ProSummonersController BuildController(
        TranscendenceContext db,
        IRefreshLockRepository refreshLockRepository,
        IBackgroundJobClient backgroundJobClient,
        IAdminAuditService? adminAuditService = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "admin@test.local")
        ], "test"));
        httpContext.Request.Headers["x-trn-request-id"] = "req-123";

        return new ProSummonersController(
            db,
            adminAuditService ?? Mock.Of<IAdminAuditService>(),
            backgroundJobClient,
            refreshLockRepository,
            Mock.Of<IRiotAccountService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static TranscendenceContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TranscendenceContext(options);
    }

    private static TrackedProSummoner SeedTrackedSummoner(TranscendenceContext db)
    {
        var tracked = new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = $"puuid-{Guid.NewGuid():N}",
            PlatformRegion = "NA1",
            GameName = "Name",
            TagLine = "Tag",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.TrackedProSummoners.Add(tracked);
        return tracked;
    }

    private sealed class StaticUrlHelper(string url) : IUrlHelper
    {
        private static readonly ActionContext StaticActionContext =
            new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

        public ActionContext ActionContext => StaticActionContext;

        public string? Action(UrlActionContext actionContext)
        {
            return url;
        }

        public string? Content(string? contentPath)
        {
            return contentPath;
        }

        public bool IsLocalUrl(string? urlToTest)
        {
            return true;
        }

        public string? Link(string? routeName, object? values)
        {
            return url;
        }

        public string? RouteUrl(UrlRouteContext routeContext)
        {
            return url;
        }
    }
}
