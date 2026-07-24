namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public sealed class ProRosterDiscoveryOptions
{
    public string Endpoint { get; set; } = "https://lol.fandom.com/api.php";
    public int PageSize { get; set; } = 500;
    public int MaxPages { get; set; } = 4;
    public int PageDelaySeconds { get; set; } = 65;
}
