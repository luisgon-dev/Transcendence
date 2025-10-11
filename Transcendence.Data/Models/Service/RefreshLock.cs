namespace Transcendence.Data.Models.Service;

public class RefreshLock
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LockedUntilUtc { get; set; }
}
