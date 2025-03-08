namespace ProjectSyndraBackend.Service.Services.Jobs;

public interface IJobTask
{
    Task Execute(CancellationToken stoppingToken);
}