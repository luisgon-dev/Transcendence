using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.Auth;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public class AdminAuditService(TranscendenceContext db) : IAdminAuditService
{
    public async Task WriteAsync(AdminAuditWriteRequest request, CancellationToken ct = default)
    {
        var metadataJson = request.Metadata == null ? null : JsonSerializer.Serialize(request.Metadata);

        db.AdminAuditEvents.Add(new AdminAuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserAccountId = request.ActorUserAccountId,
            ActorEmail = request.ActorEmail,
            Action = request.Action,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            RequestId = request.RequestId,
            IsSuccess = request.IsSuccess,
            MetadataJson = metadataJson,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AdminAuditEntryDto>> ListRecentAsync(int limit, CancellationToken ct = default)
    {
        var bounded = Math.Clamp(limit, 1, 500);
        return await db.AdminAuditEvents
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(bounded)
            .Select(x => new AdminAuditEntryDto(
                x.Id,
                x.ActorUserAccountId,
                x.ActorEmail,
                x.Action,
                x.TargetType,
                x.TargetId,
                x.RequestId,
                x.IsSuccess,
                x.MetadataJson,
                x.CreatedAtUtc))
            .ToListAsync(ct);
    }
}
