namespace Transcendence.Data.Models.Service;

public class RefreshLock
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LockedUntilUtc { get; set; }

    /// <summary>
    /// Per-acquisition fencing token, rotated on every successful <c>TryAcquireOwnedAsync</c>. A holder
    /// releases only when it still owns this token, so a stale holder whose lease already expired (and
    /// was re-acquired by someone else) can no longer release the new owner's lock. Null for locks taken
    /// via the unfenced <c>TryAcquireAsync</c> path.
    /// </summary>
    public Guid? OwnerToken { get; set; }
}