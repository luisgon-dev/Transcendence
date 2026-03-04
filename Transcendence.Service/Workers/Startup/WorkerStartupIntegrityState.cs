namespace Transcendence.Service.Workers.Startup;

public enum WorkerStartupIntegrityStatus
{
    NotStarted = 0,
    Healthy = 1,
    Degraded = 2,
    FailFast = 3
}

public sealed record WorkerStartupIntegrityResult(
    WorkerStartupIntegrityStatus Status,
    string ProfileName,
    int Attempt,
    int MaxAttempts,
    IReadOnlyList<string> VerifiedMandatoryJobIds,
    IReadOnlyList<string> MandatoryFailures,
    IReadOnlyList<string> OptionalFailures)
{
    public static WorkerStartupIntegrityResult NotStarted { get; } = new(
        WorkerStartupIntegrityStatus.NotStarted,
        ProfileName: "unknown",
        Attempt: 0,
        MaxAttempts: 0,
        VerifiedMandatoryJobIds: [],
        MandatoryFailures: [],
        OptionalFailures: []);
}

public sealed class WorkerStartupIntegrityState
{
    private WorkerStartupIntegrityResult current = WorkerStartupIntegrityResult.NotStarted;

    public WorkerStartupIntegrityResult Current => Volatile.Read(ref current);

    public void Set(WorkerStartupIntegrityResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Volatile.Write(ref current, result);
    }
}
