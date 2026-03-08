using Camille.Enums;
using Transcendence.Data.Models.Tft.Match;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftMatchService
{
    Task<TftMatch?> GetMatchDetailsAsync(
        string matchId,
        RegionalRoute regionalRoute,
        PlatformRoute platformRoute,
        CancellationToken ct = default);
}
