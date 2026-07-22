using System.Net;
using Camille.RiotGames.Util;

namespace Transcendence.Service.Core.Services.RiotApi;

/// <summary>
/// Classifies a final 429 surfaced by Camille after its own retry policy is exhausted and derives a
/// bounded regional backoff from Riot's Retry-After response header.
/// </summary>
public static class RiotRateLimitHandling
{
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromMinutes(10);

    public static bool TryGetRetryAfter(Exception exception, out TimeSpan retryAfter)
    {
        retryAfter = default;
        if (exception is not RiotResponseException riotException
            || riotException.GetResponse()?.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return false;
        }

        var response = riotException.GetResponse()!;
        var header = response.Headers.RetryAfter;
        var candidate = header?.Delta;
        if (!candidate.HasValue && header?.Date is { } retryAt)
            candidate = retryAt - DateTimeOffset.UtcNow;

        retryAfter = candidate.GetValueOrDefault(DefaultRetryAfter);
        if (retryAfter <= TimeSpan.Zero)
            retryAfter = TimeSpan.FromSeconds(1);
        if (retryAfter > MaximumRetryAfter)
            retryAfter = MaximumRetryAfter;

        return true;
    }
}
