using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Database.Interfaces;

namespace Transcendence.Service.Core.Services.Database.Implementations;

public sealed class DatabaseHealthProbe(TranscendenceContext db) : IDatabaseHealthProbe
{
    public Task<bool> CanConnectAsync(CancellationToken ct = default) =>
        db.Database.CanConnectAsync(ct);
}
