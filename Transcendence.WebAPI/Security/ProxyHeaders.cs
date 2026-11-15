using Microsoft.AspNetCore.HttpOverrides;

namespace Transcendence.WebAPI.Security;

public static class ProxyHeaders
{
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration, bool required)
    {
        var networks = configuration.GetSection("Security:TrustedProxyNetworks").Get<string[]>() ?? [];
        if (required && networks.Length == 0)
            throw new InvalidOperationException("Security:TrustedProxyNetworks must be configured in production.");
        var limit = configuration.GetValue("Security:ForwardedHeaderLimit", 1);
        if (limit is < 1 or > 2)
            throw new InvalidOperationException("Security:ForwardedHeaderLimit must be 1 or 2.");
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = limit;
        foreach (var network in networks)
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
}
