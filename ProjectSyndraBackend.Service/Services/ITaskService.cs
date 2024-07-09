namespace ProjectSyndraBackend.Service.Services;

public interface ITaskService
{
    Task ExecuteAsync(CancellationToken stoppingToken);
    
    int Interval { get; }
    
}