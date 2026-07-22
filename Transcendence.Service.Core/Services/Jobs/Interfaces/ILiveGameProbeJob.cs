using Hangfire;

namespace Transcendence.Service.Core.Services.Jobs.Interfaces;

public interface ILiveGameProbeJob
{
    [Queue("refresh-high")]
    Task ProbeAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        string lockHandle,
        CancellationToken ct = default);
}
