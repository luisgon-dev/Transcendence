namespace Transcendence.Data.Models.Service.Helpers;

public class UnitWinPercent
{
    public int Id { get; set; }
    public int NumberOfGames { get; set; }
    public required float WinRate { get; set; }
    public required string Type { get; set; }
    public required string Unit { get; set; }
}