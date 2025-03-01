namespace ProjectSyndraBackend.Service.Services.Recurring_Jobs;

public interface IJobTask
{
    Task Execute(CancellationToken stoppingToken);
}