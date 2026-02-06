namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

public class DataAgeMetadata
{
    public DateTime FetchedAt { get; set; }
    public TimeSpan Age => DateTime.UtcNow - FetchedAt;
    public string AgeDescription => Age.TotalMinutes < 5
        ? "Just now"
        : Age.TotalHours < 1
            ? $"{(int)Age.TotalMinutes} minutes ago"
            : Age.TotalDays < 1
                ? $"{(int)Age.TotalHours} hours ago"
                : $"{(int)Age.TotalDays} days ago";
}
