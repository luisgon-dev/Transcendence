using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Transcendence.Service.Core.Services.Diagnostics;

public sealed class AlertOptions
{
    public AlertWebhookOptions Webhook { get; set; } = new();

    /// <summary>New failed jobs within one poll interval above which a failed-jobs spike alert fires.
    /// A delta, not a cumulative count — Hangfire retains old failures, so the absolute count is
    /// meaningless (it only ever climbs).</summary>
    public int FailedJobSpikeThreshold { get; set; } = 50;

    /// <summary>Discovery queue depth above which a stuck-backlog alert fires.</summary>
    public long DiscoveryQueueDepthThreshold { get; set; } = 10_000;

    /// <summary>How long a given condition stays silenced after firing, so it does not re-alert every run.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(30);
}

public sealed class AlertWebhookOptions
{
    /// <summary>Discord/Slack-compatible incoming webhook URL. Empty → alerts are logged only.</summary>
    public string Url { get; set; } = string.Empty;
}

public interface IAlertNotifier
{
    /// <summary>Send an operational alert. No-ops to a log warning when no webhook URL is configured.</summary>
    Task SendAsync(string title, string body, CancellationToken cancellationToken = default);
}

public sealed class WebhookAlertNotifier(
    IHttpClientFactory httpClientFactory,
    IOptions<AlertOptions> options,
    ILogger<WebhookAlertNotifier> logger) : IAlertNotifier
{
    public async Task SendAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        var url = options.Value.Webhook.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogWarning("ALERT (no webhook configured): {Title} — {Body}", title, body);
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            // Discord-compatible payload ("content"); Slack incoming webhooks accept "text" instead.
            var payload = new { content = $"**{title}**\n{body}" };
            using var response = await client.PostAsJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Alert webhook returned {Status} for {Title}", (int)response.StatusCode, title);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to post alert webhook: {Title}", title);
        }
    }
}
