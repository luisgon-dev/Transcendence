using Transcendence.WebAPI.Controllers;

namespace Transcendence.Service.Core.Services.ProSummoners.Interfaces;

public interface ITrackedProSummonerService
{
    Task<IReadOnlyList<TrackedProSummonerDto>> ListAsync(bool? isActive, CancellationToken ct = default);

    Task<TrackedProSummonerDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<TrackedProCreateResult> CreateAsync(
        UpsertTrackedProSummonerRequest request,
        CancellationToken ct = default);

    Task<TrackedProSummonerDto?> UpdateAsync(
        Guid id,
        UpsertTrackedProSummonerRequest request,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ProPlayerDiscoveryCandidateDto>> ListCandidatesAsync(
        string status,
        CancellationToken ct = default);

    Task<TrackedProCreateResult> ApproveCandidateAsync(
        Guid id,
        ApproveProPlayerCandidateRequest request,
        CancellationToken ct = default);

    Task<bool> RejectCandidateAsync(Guid id, CancellationToken ct = default);
}

public sealed record TrackedProCreateResult(TrackedProSummonerDto? Value, string? ValidationError)
{
    public bool IsSuccess => Value is not null;

    public static TrackedProCreateResult Success(TrackedProSummonerDto value) => new(value, null);

    public static TrackedProCreateResult Invalid(string message) => new(null, message);
}
