using FluentAssertions;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Tests;

public class WorkerSchedulingPolicyTests
{
    [Fact]
    public void BuildDescriptors_DefaultAndDevelopmentProfiles_KeepMandatoryBaselineParity()
    {
        var policy = CreatePolicyWithDevelopmentOverrides(new WorkerSchedulingProfileDefinition
        {
            JobOverrides = new Dictionary<string, WorkerSchedulingJobOverrideOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [WorkerRecurringJobPolicy.MatchTimelineBackfillJobId] = new() { Enabled = false },
                [WorkerRecurringJobPolicy.RefreshChampionAnalyticsAdaptiveJobId] =
                    new() { Cron = "*/10 * * * *" }
            }
        });

        var baseSchedule = new WorkerJobScheduleOptions
        {
            Profile = "default",
            DefaultProfile = "default"
        };
        var developmentSchedule = new WorkerJobScheduleOptions
        {
            Profile = "development",
            DefaultProfile = "default"
        };

        var defaultDescriptors = policy.BuildDescriptors(baseSchedule);
        var developmentDescriptors = policy.BuildDescriptors(developmentSchedule);

        var defaultMandatory = defaultDescriptors
            .Where(descriptor => descriptor.IsMandatoryBaseline)
            .Select(descriptor => descriptor.JobId)
            .OrderBy(jobId => jobId)
            .ToArray();
        var developmentMandatory = developmentDescriptors
            .Where(descriptor => descriptor.IsMandatoryBaseline)
            .Select(descriptor => descriptor.JobId)
            .OrderBy(jobId => jobId)
            .ToArray();

        defaultMandatory.Should().Equal(developmentMandatory);
    }

    [Fact]
    public void BuildDescriptors_DevelopmentCronOverride_PreservesMandatoryClassification()
    {
        var policy = CreatePolicyWithDevelopmentOverrides(new WorkerSchedulingProfileDefinition
        {
            JobOverrides = new Dictionary<string, WorkerSchedulingJobOverrideOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [WorkerRecurringJobPolicy.DetectPatchJobId] =
                    new() { Cron = "*/11 * * * *" }
            }
        });

        var schedule = new WorkerJobScheduleOptions
        {
            Profile = "development",
            DefaultProfile = "default"
        };

        var descriptor = policy.BuildDescriptors(schedule)
            .Single(x => x.JobId == WorkerRecurringJobPolicy.DetectPatchJobId);

        descriptor.CronExpression.Should().Be("*/11 * * * *");
        descriptor.IsMandatoryBaseline.Should().BeTrue();
        descriptor.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildDescriptors_DefaultProfile_RegistersRefreshLockLifecycleCleanupByDefault()
    {
        var policy = CreatePolicyWithDevelopmentOverrides(new WorkerSchedulingProfileDefinition());
        var schedule = new WorkerJobScheduleOptions
        {
            Profile = "default",
            DefaultProfile = "default",
            RefreshLockLifecycleCleanupCron = "*/7 * * * *",
            EnableRefreshLockLifecycleCleanup = true
        };

        var descriptor = policy.BuildDescriptors(schedule)
            .Single(x => x.JobId == WorkerRecurringJobPolicy.RefreshLockLifecycleCleanupJobId);

        descriptor.CronExpression.Should().Be("*/7 * * * *");
        descriptor.CronSource.Should().Be("Jobs:Schedule:RefreshLockLifecycleCleanupCron");
        descriptor.IsEnabled.Should().BeTrue();
        descriptor.IsMandatoryBaseline.Should().BeTrue();
    }

    [Fact]
    public void BuildDescriptors_DevelopmentProfileOverride_CanDisableRefreshLockLifecycleCleanup()
    {
        var policy = CreatePolicyWithDevelopmentOverrides(new WorkerSchedulingProfileDefinition
        {
            JobOverrides = new Dictionary<string, WorkerSchedulingJobOverrideOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [WorkerRecurringJobPolicy.RefreshLockLifecycleCleanupJobId] =
                    new() { Enabled = false, Cron = "*/13 * * * *" }
            }
        });

        var schedule = new WorkerJobScheduleOptions
        {
            Profile = "development",
            DefaultProfile = "default",
            EnableRefreshLockLifecycleCleanup = true,
            RefreshLockLifecycleCleanupCron = "*/7 * * * *"
        };

        var descriptor = policy.BuildDescriptors(schedule)
            .Single(x => x.JobId == WorkerRecurringJobPolicy.RefreshLockLifecycleCleanupJobId);

        descriptor.CronExpression.Should().Be("*/13 * * * *");
        descriptor.CronSource.Should()
            .Be("Jobs:SchedulingProfiles:Profiles:development:JobOverrides:refresh-lock-lifecycle-cleanup:Cron");
        descriptor.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildDescriptors_StableProfile_EnablesCoverageFirstAnalyticsJobs()
    {
        var policy = new WorkerRecurringJobPolicy(Options.Create(new WorkerSchedulingProfileOptions
        {
            Profiles = new Dictionary<string, WorkerSchedulingProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["stable"] = new()
                {
                    JobOverrides = new Dictionary<string, WorkerSchedulingJobOverrideOptions>(StringComparer.OrdinalIgnoreCase)
                    {
                        [WorkerRecurringJobPolicy.RefreshChampionAnalyticsJobId] = new() { Enabled = false },
                        [WorkerRecurringJobPolicy.RefreshChampionAnalyticsAdaptiveJobId] = new() { Enabled = true, Cron = "*/5 * * * *" },
                        [WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId] = new() { Enabled = true, Cron = "*/2 * * * *" },
                        [WorkerRecurringJobPolicy.SummonerMaintenanceJobId] = new() { Enabled = true, Cron = "*/5 * * * *" },
                        [WorkerRecurringJobPolicy.MatchTimelineBackfillJobId] = new() { Enabled = true, Cron = "0 * * * *" },
                        [WorkerRecurringJobPolicy.RuneSelectionIntegrityBackfillJobId] = new() { Enabled = false },
                        [WorkerRecurringJobPolicy.PollLiveGamesJobId] = new() { Enabled = false },
                        [WorkerRecurringJobPolicy.HighEloProfileRefreshJobId] = new() { Enabled = true, Cron = "0 */2 * * *" }
                    }
                }
            }
        }));

        var schedule = new WorkerJobScheduleOptions
        {
            Profile = "stable",
            DefaultProfile = "stable"
        };

        var descriptors = policy.BuildDescriptors(schedule);

        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.DetectPatchJobId && x.IsEnabled && x.IsMandatoryBaseline);
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.RetryFailedMatchesJobId && x.IsEnabled && x.IsMandatoryBaseline);
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId && x.IsEnabled && x.IsMandatoryBaseline);
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.RefreshLockLifecycleCleanupJobId && x.IsEnabled && x.IsMandatoryBaseline);

        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.RefreshChampionAnalyticsJobId && !x.IsEnabled);
        descriptors.Should().Contain(x =>
            x.JobId == WorkerRecurringJobPolicy.RefreshBuildResourceAnalyticsJobId &&
            x.IsEnabled &&
            x.CronExpression == "40 * * * *");
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.RefreshChampionAnalyticsAdaptiveJobId && x.IsEnabled && x.CronExpression == "*/5 * * * *");
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId && x.IsEnabled && x.CronExpression == "*/2 * * * *");
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.SummonerMaintenanceJobId && x.IsEnabled && x.CronExpression == "*/5 * * * *");
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.MatchTimelineBackfillJobId && x.IsEnabled && x.CronExpression == "0 * * * *");
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.HighEloProfileRefreshJobId && x.IsEnabled && x.CronExpression == "0 */2 * * *");
        descriptors.Should().Contain(x => x.JobId == WorkerRecurringJobPolicy.PollLiveGamesJobId && !x.IsEnabled);
    }

    private static WorkerRecurringJobPolicy CreatePolicyWithDevelopmentOverrides(
        WorkerSchedulingProfileDefinition developmentProfile) =>
        new(Options.Create(new WorkerSchedulingProfileOptions
        {
            Profiles = new Dictionary<string, WorkerSchedulingProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["development"] = developmentProfile
            }
        }));
}
