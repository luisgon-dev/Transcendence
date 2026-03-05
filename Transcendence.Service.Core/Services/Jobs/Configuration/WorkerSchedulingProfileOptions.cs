namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class WorkerSchedulingProfileOptions
{
    public Dictionary<string, WorkerSchedulingProfileDefinition> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public class WorkerSchedulingProfileDefinition
{
    public Dictionary<string, WorkerSchedulingJobOverrideOptions> JobOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public class WorkerSchedulingJobOverrideOptions
{
    public bool? Enabled { get; set; }
    public string? Cron { get; set; }
    public bool? MandatoryBaseline { get; set; }
}
