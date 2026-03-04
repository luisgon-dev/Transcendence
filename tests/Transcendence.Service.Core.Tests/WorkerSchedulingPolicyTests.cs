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
