using Camille.Enums;

namespace Transcendence.Service.Core.Services.Jobs;

public static class RefreshLockKeys
{
    public const string SummonerRefreshPrefix = "summoner-refresh:";
    public const string ApiPriorityRefreshPrefix = "refresh-priority:api:";
    public const string StarvationGuardrailCatchUpPrefix = "refresh-priority:guardrail:catchup:";
    public const string StarvationGuardrailCooldownPrefix = "refresh-priority:guardrail:cooldown:";
    public const string ProducerPacingPrefix = "producer:pacing:";

    public static string BuildCanonicalIdentity(PlatformRoute platform, string gameName, string tagLine)
    {
        return BuildCanonicalIdentity(platform.ToString(), gameName, tagLine);
    }

    public static string BuildCanonicalIdentity(string platformRegion, string gameName, string tagLine)
    {
        return
            $"{NormalizePlatform(platformRegion)}:{NormalizeRiotIdPart(gameName)}:{NormalizeRiotIdPart(tagLine)}";
    }

    public static string BuildSummonerRefreshKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return $"{SummonerRefreshPrefix}{BuildCanonicalIdentity(platform, gameName, tagLine)}";
    }

    public static string BuildApiPriorityKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return $"{ApiPriorityRefreshPrefix}{BuildCanonicalIdentity(platform, gameName, tagLine)}";
    }

    public static string BuildStarvationGuardrailCatchUpKey(string producerKey)
    {
        return $"{StarvationGuardrailCatchUpPrefix}{NormalizeGuardrailProducerKey(producerKey)}";
    }

    public static string BuildStarvationGuardrailCooldownKey(string producerKey)
    {
        return $"{StarvationGuardrailCooldownPrefix}{NormalizeGuardrailProducerKey(producerKey)}";
    }

    // Self-pacing slot for a recurring producer: a single heartbeat cron fires the producer often, and
    // the producer acquires this lock with a TTL equal to its current desired interval (shorter during a
    // new-patch ramp, longer in steady state). While the lock is held the producer skips, so the JOB —
    // not a second cron — decides its effective cadence.
    public static string BuildProducerPacingKey(string producerKey)
    {
        return $"{ProducerPacingPrefix}{NormalizeGuardrailProducerKey(producerKey)}";
    }

    public static string NormalizePlatform(string platformRegion)
    {
        return platformRegion.Trim().ToUpperInvariant();
    }

    public static string NormalizeRiotIdPart(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeGuardrailProducerKey(string producerKey)
    {
        if (string.IsNullOrWhiteSpace(producerKey))
            return "default";

        return producerKey.Trim().ToLowerInvariant().Replace(':', '-');
    }
}
