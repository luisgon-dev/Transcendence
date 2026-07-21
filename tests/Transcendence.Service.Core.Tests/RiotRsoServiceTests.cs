using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Implementations;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Tests;

public sealed class RiotRsoServiceTests
{
    [Fact]
    public void Authorization_requires_complete_configuration_and_encodes_state()
    {
        var disabled = CreateService(new RiotRsoOptions(), new StubHandler(_ => new HttpResponseMessage()));
        var action = () => disabled.CreateAuthorization(new string('s', 32));
        action.Should().Throw<RiotRsoUnavailableException>();

        var enabled = CreateService(ConfiguredOptions(), new StubHandler(_ => new HttpResponseMessage()));
        var response = enabled.CreateAuthorization("state-with-/+symbols-and-enough-entropy");

        response.AuthorizationUrl.Should().StartWith("https://auth.riotgames.com/authorize?");
        response.AuthorizationUrl.Should().Contain("response_type=code");
        response.AuthorizationUrl.Should().Contain("scope=openid%20offline_access");
        response.AuthorizationUrl.Should().Contain("state=state-with-%2F%2Bsymbols-and-enough-entropy");
    }

    [Fact]
    public async Task Login_exchanges_code_with_basic_auth_and_creates_verified_riot_only_account()
    {
        string? tokenAuthorization = null;
        string? tokenForm = null;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                tokenAuthorization = request.Headers.Authorization?.Parameter;
                tokenForm = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(HttpStatusCode.OK, """{"access_token":"riot-access","token_type":"Bearer","expires_in":3600}""");
            }
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("riot-access");
            return Json(HttpStatusCode.OK, """{"puuid":"verified-puuid","gameName":"Kronic","tagLine":"NA1"}""");
        });
        var users = new Mock<IUserAccountRepository>();
        var links = new Mock<IUserRiotAccountRepository>();
        links.Setup(repository => repository.GetByPuuidAsync("verified-puuid", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserRiotAccount?)null);
        UserAccount? createdUser = null;
        UserRiotAccount? createdLink = null;
        users.Setup(repository => repository.AddUserAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((user, _) => createdUser = user)
            .Returns(Task.CompletedTask);
        links.Setup(repository => repository.AddAsync(It.IsAny<UserRiotAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserRiotAccount, CancellationToken>((link, _) => createdLink = link)
            .Returns(Task.CompletedTask);
        var auth = new Mock<IUserAuthService>();
        auth.Setup(service => service.SignInExternalAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthTokenResponse("site-access", "site-refresh", DateTime.UtcNow.AddMinutes(15)));
        var service = CreateService(ConfiguredOptions(), handler, users, links, auth);

        var result = await service.CompleteLoginAsync("one-time-code", "na");

        result.CreatedAccount.Should().BeTrue();
        result.RiotAccount.GameName.Should().Be("Kronic");
        result.RiotAccount.PlatformRegion.Should().Be("NA1");
        result.RiotAccount.CanUnlink.Should().BeFalse();
        createdUser.Should().NotBeNull();
        createdUser!.DisplayName.Should().Be("Kronic#NA1");
        createdUser.PasswordHash.Should().BeEmpty();
        createdUser.Email.Should().EndWith("@rso.invalid");
        createdLink!.Puuid.Should().Be("verified-puuid");
        Encoding.UTF8.GetString(Convert.FromBase64String(tokenAuthorization!))
            .Should().Be("client-id:client-secret");
        tokenForm.Should().Contain("grant_type=authorization_code");
        tokenForm.Should().Contain("code=one-time-code");
    }

    [Fact]
    public async Task Unlink_refuses_to_strand_riot_only_accounts()
    {
        var link = new UserRiotAccount
        {
            UserAccountId = Guid.NewGuid(),
            Puuid = "puuid",
            GameName = "Player",
            TagLine = "NA1",
            PlatformRegion = "NA1",
            UserAccount = new UserAccount
            {
                Email = "riot@rso.invalid",
                EmailNormalized = "RIOT@RSO.INVALID",
                PasswordHash = string.Empty
            }
        };
        var links = new Mock<IUserRiotAccountRepository>();
        links.Setup(repository => repository.GetByUserIdAsync(link.UserAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);
        var service = CreateService(
            ConfiguredOptions(),
            new StubHandler(_ => new HttpResponseMessage()),
            links: links);

        var removed = await service.UnlinkAsync(link.UserAccountId);

        removed.Should().BeFalse();
        links.Verify(repository => repository.Remove(It.IsAny<UserRiotAccount>()), Times.Never);
    }

    private static RiotRsoService CreateService(
        RiotRsoOptions options,
        HttpMessageHandler handler,
        Mock<IUserAccountRepository>? users = null,
        Mock<IUserRiotAccountRepository>? links = null,
        Mock<IUserAuthService>? auth = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new RiotRsoService(
            (users ?? new Mock<IUserAccountRepository>()).Object,
            (links ?? new Mock<IUserRiotAccountRepository>()).Object,
            (auth ?? new Mock<IUserAuthService>()).Object,
            factory.Object,
            Options.Create(options),
            NullLogger<RiotRsoService>.Instance);
    }

    private static RiotRsoOptions ConfiguredOptions() => new()
    {
        Enabled = true,
        ClientId = "client-id",
        ClientSecret = "client-secret",
        RedirectUri = "https://transcend.example/api/session/riot/callback",
        AuthorizationEndpoint = "https://auth.riotgames.com/authorize",
        TokenEndpoint = "https://auth.riotgames.com/token",
        AccountEndpoint = "https://americas.api.riotgames.com/riot/account/v1/accounts/me"
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
