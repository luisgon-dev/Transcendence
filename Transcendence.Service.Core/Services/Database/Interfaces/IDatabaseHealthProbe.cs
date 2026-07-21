namespace Transcendence.Service.Core.Services.Database.Interfaces;

public interface IDatabaseHealthProbe
{
    Task<bool> CanConnectAsync(CancellationToken ct = default);
}
