using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Tests;

public class ProxyHeadersTests
{
    [Theory]
    [InlineData(1, "203.0.113.10, 192.168.1.4", "192.168.1.4")]
    [InlineData(2, "203.0.113.10, 192.168.1.4", "203.0.113.10")]
    [InlineData(2, "198.51.100.99, 203.0.113.10, 192.168.1.4", "203.0.113.10")]
    [InlineData(2, "198.51.100.99, 192.168.1.55", "192.168.1.55")]
    public async Task OnlyKnownProxyHopsCanForwardIdentity(int limit, string forwarded, string expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Security:TrustedProxyNetworks:0"] = "172.18.0.0/16",
            ["Security:TrustedProxyNetworks:1"] = "192.168.1.4/32",
            ["Security:ForwardedHeaderLimit"] = limit.ToString()
        }).Build();
        var options = new ForwardedHeadersOptions();
        ProxyHeaders.Configure(options, config, true);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.2");
        context.Request.Headers["X-Forwarded-For"] = forwarded;
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask,
            NullLoggerFactory.Instance, Options.Create(options));

        await middleware.Invoke(context);

        Assert.Equal(IPAddress.Parse(expected), context.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void UnboundedOrUnexpectedHopCountsAreRejected(int limit)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Security:ForwardedHeaderLimit"] = limit.ToString()
        }).Build();
        Assert.Throws<InvalidOperationException>(() => ProxyHeaders.Configure(new(), config, false));
    }
}
