namespace Transcendence.Service.Services.Jobs;

public interface IJobTask
{
    Task Execute(CancellationToken stoppingToken);
}