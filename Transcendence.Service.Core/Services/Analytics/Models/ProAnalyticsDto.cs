namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// A single champion's pick/play stats among tracked pro / high-elo players.
/// </summary>
public record ProChampionPlayrateDto(
    int ChampionId,
    int Games,
    int Wins,
    double WinRate,        // 0.0 to 1.0
    int UniquePlayers);    // Distinct tracked players who played this champion

/// <summary>
/// Champions ranked by how often tracked pros / high-elo players pick them.
/// Scope selects the roster: "pro" (IsPro), "highelo" (IsHighEloOtp), or "all" (either).
/// </summary>
public record ProChampionPlayrateResponse(
    string Patch,
    string Region,
    string Scope,
    List<ProChampionPlayrateDto> Champions,
    AnalyticsSampleMetadata? Sample = null);

/// <summary>
/// A public-facing tracked pro player (no internal identifiers exposed).
/// </summary>
public record ProPlayerDto(
    string? ProName,
    string? TeamName,
    string PlatformRegion,
    string? GameName,
    string? TagLine);

/// <summary>
/// The public roster of tracked pro players.
/// </summary>
public record ProRosterResponse(
    string Region,
    List<ProPlayerDto> Players);
