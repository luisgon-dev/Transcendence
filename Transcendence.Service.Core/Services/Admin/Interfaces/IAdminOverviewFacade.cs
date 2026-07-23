using Transcendence.Service.Core.Services.Admin.Models;

namespace Transcendence.Service.Core.Services.Admin.Interfaces;

/// <summary>
/// Encapsulates the read-only system snapshot logic extracted verbatim from
/// AdminOperationsController's <c>overview</c> and <c>metrics/analysis</c> actions (P10.1). Owns the
/// EF context, ingestion/multi-region options and JobStorage that those snapshots read.
/// </summary>
public interface IAdminOverviewFacade
{
    Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken ct);

    Task<AdminAnalysisMetricsResponse> GetAnalysisMetricsAsync(CancellationToken ct);
}
