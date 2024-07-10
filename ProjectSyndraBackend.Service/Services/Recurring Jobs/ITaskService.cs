namespace ProjectSyndraBackend.Service.Services.Recurring_Jobs;

public interface ITaskService
{
    int Interval { get; }
    Task ExecuteAsync(CancellationToken stoppingToken);
}