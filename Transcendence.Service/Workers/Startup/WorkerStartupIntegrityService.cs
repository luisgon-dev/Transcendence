using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Workers.Startup;

public interface IWorkerStartupIntegrityService
{
    Task<WorkerStartupIntegrityResult> EvaluateAsync(CancellationToken cancellationToken);
}

public sealed class WorkerStartupIntegrityService(
    ILogger<WorkerStartupIntegrityService> logger,
    IOptions<WorkerJobScheduleOptions> optionsAccessor,
    IWorkerRecurringJobPolicy recurringJobPolicy,
    IRecurringJobManager recurringJobManager,
    JobStorage jobStorage,
    WorkerStartupIntegrityState startupIntegrityState)
    : IWorkerStartupIntegrityService
{
    public async Task<WorkerStartupIntegrityResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        var schedule = optionsAccessor.Value;
        var descriptors = recurringJobPolicy.BuildDescriptors(schedule);
        var profileName = recurringJobPolicy.ResolveProfile(schedule);

        var maxAttempts = Math.Max(1, schedule.StartupIntegrityMaxAttempts);
        var retryBackoffSeconds = Math.Max(0, schedule.StartupIntegrityRetryBackoffSeconds);

        WorkerStartupIntegrityResult? lastResult = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = EvaluateAttempt(descriptors, profileName, attempt, maxAttempts);
            lastResult = result;

            if (result.Status != WorkerStartupIntegrityStatus.FailFast)
            {
                startupIntegrityState.Set(result);
                LogFinalResult(result);
                return result;
            }

            if (attempt < maxAttempts && retryBackoffSeconds > 0)
            {
                var delay = TimeSpan.FromSeconds(retryBackoffSeconds * attempt);
                logger.LogWarning(
                    "Worker startup integrity attempt {Attempt}/{MaxAttempts} failed mandatory verification. Retrying after {DelaySeconds}s.",
                    attempt,
                    maxAttempts,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        lastResult ??= new WorkerStartupIntegrityResult(
            WorkerStartupIntegrityStatus.FailFast,
            profileName,
            maxAttempts,
            maxAttempts,
            [],
            ["startup-integrity: no attempts were executed."],
            []);

        startupIntegrityState.Set(lastResult);
        LogFinalResult(lastResult);
        return lastResult;
    }

    private WorkerStartupIntegrityResult EvaluateAttempt(
        IReadOnlyList<WorkerRecurringJobDescriptor> descriptors,
        string profileName,
        int attempt,
        int maxAttempts)
    {
        var mandatoryFailures = new List<string>();
        var optionalFailures = new List<string>();
        var verifiedMandatoryJobIds = new List<string>();

        foreach (var descriptor in descriptors)
        {
            if (descriptor.IsMandatoryBaseline && !descriptor.IsEnabled)
            {
                mandatoryFailures.Add(
                    $"{descriptor.JobId}: mandatory recurring job is disabled by scheduling policy.");
            }

            if (!descriptor.IsEnabled)
            {
                TryRemoveRecurringJob(descriptor, mandatoryFailures, optionalFailures);
                continue;
            }

            TryConfigureRecurringJob(descriptor, mandatoryFailures, optionalFailures);
        }

        VerifyMandatoryRecurringJobs(descriptors, mandatoryFailures, verifiedMandatoryJobIds);

        var status = mandatoryFailures.Count > 0
            ? WorkerStartupIntegrityStatus.FailFast
            : optionalFailures.Count > 0
                ? WorkerStartupIntegrityStatus.Degraded
                : WorkerStartupIntegrityStatus.Healthy;

        logger.LogInformation(
            "Worker startup integrity attempt {Attempt}/{MaxAttempts} completed with status {Status}. Mandatory failures: {MandatoryCount}. Optional failures: {OptionalCount}. Verified mandatory jobs: {VerifiedMandatoryJobs}.",
            attempt,
            maxAttempts,
            status,
            mandatoryFailures.Count,
            optionalFailures.Count,
            verifiedMandatoryJobIds.Count);

        return new WorkerStartupIntegrityResult(
            status,
            profileName,
            attempt,
            maxAttempts,
            verifiedMandatoryJobIds,
            mandatoryFailures,
            optionalFailures);
    }

    private void TryConfigureRecurringJob(
        WorkerRecurringJobDescriptor descriptor,
        ICollection<string> mandatoryFailures,
        ICollection<string> optionalFailures)
    {
        try
        {
            descriptor.Apply(recurringJobManager);
        }
        catch (Exception ex)
        {
            RegisterFailure(
                descriptor,
                $"registration failed ({ex.GetType().Name})",
                mandatoryFailures,
                optionalFailures);
        }
    }

    private void TryRemoveRecurringJob(
        WorkerRecurringJobDescriptor descriptor,
        ICollection<string> mandatoryFailures,
        ICollection<string> optionalFailures)
    {
        try
        {
            recurringJobManager.RemoveIfExists(descriptor.JobId);
        }
        catch (Exception ex)
        {
            RegisterFailure(
                descriptor,
                $"failed to remove disabled recurring job ({ex.GetType().Name})",
                mandatoryFailures,
                optionalFailures);
        }
    }

    private void VerifyMandatoryRecurringJobs(
        IReadOnlyList<WorkerRecurringJobDescriptor> descriptors,
        ICollection<string> mandatoryFailures,
        ICollection<string> verifiedMandatoryJobIds)
    {
        var mandatoryDescriptors = descriptors
            .Where(descriptor => descriptor.IsMandatoryBaseline)
            .ToArray();

        if (mandatoryDescriptors.Length == 0)
            return;

        try
        {
            using var connection = jobStorage.GetConnection();

            foreach (var descriptor in mandatoryDescriptors)
            {
                if (!descriptor.IsEnabled)
                    continue;

                var hash = connection.GetAllEntriesFromHash($"recurring-job:{descriptor.JobId}");
                if (hash == null || hash.Count == 0)
                {
                    mandatoryFailures.Add(
                        $"{descriptor.JobId}: recurring-job hash is missing after registration.");
                    continue;
                }

                if (!hash.TryGetValue("Cron", out var cron) || string.IsNullOrWhiteSpace(cron))
                {
                    mandatoryFailures.Add(
                        $"{descriptor.JobId}: cron metadata missing from recurring-job hash.");
                    continue;
                }

                if (!hash.TryGetValue("Job", out var jobPayload) || string.IsNullOrWhiteSpace(jobPayload))
                {
                    mandatoryFailures.Add(
                        $"{descriptor.JobId}: invocation payload missing from recurring-job hash.");
                    continue;
                }

                try
                {
                    var invocation = InvocationData.DeserializePayload(jobPayload);
                    _ = invocation.DeserializeJob();
                    verifiedMandatoryJobIds.Add(descriptor.JobId);
                }
                catch (Exception ex)
                {
                    mandatoryFailures.Add(
                        $"{descriptor.JobId}: invocation payload could not be deserialized ({ex.GetType().Name}).");
                }
            }
        }
        catch (Exception ex)
        {
            mandatoryFailures.Add(
                $"hangfire-storage: recurring-job verification pipeline failed ({ex.GetType().Name}).");
        }
    }

    private static void RegisterFailure(
        WorkerRecurringJobDescriptor descriptor,
        string reason,
        ICollection<string> mandatoryFailures,
        ICollection<string> optionalFailures)
    {
        var failure = $"{descriptor.JobId}: {reason}";
        if (descriptor.IsMandatoryBaseline)
            mandatoryFailures.Add(failure);
        else
            optionalFailures.Add(failure);
    }

    private void LogFinalResult(WorkerStartupIntegrityResult result)
    {
        if (result.Status == WorkerStartupIntegrityStatus.Healthy)
        {
            logger.LogInformation(
                "Worker startup integrity succeeded for profile {Profile} on attempt {Attempt}/{MaxAttempts}.",
                result.ProfileName,
                result.Attempt,
                result.MaxAttempts);
            return;
        }

        if (result.Status == WorkerStartupIntegrityStatus.Degraded)
        {
            logger.LogWarning(
                "Worker startup integrity completed in degraded mode for profile {Profile} on attempt {Attempt}/{MaxAttempts}. Optional failures: {OptionalFailures}.",
                result.ProfileName,
                result.Attempt,
                result.MaxAttempts,
                string.Join("; ", result.OptionalFailures));
            return;
        }

        logger.LogError(
            "Worker startup integrity failed mandatory verification for profile {Profile} after attempt {Attempt}/{MaxAttempts}. Mandatory failures: {MandatoryFailures}.",
            result.ProfileName,
            result.Attempt,
            result.MaxAttempts,
            string.Join("; ", result.MandatoryFailures));
    }
}
