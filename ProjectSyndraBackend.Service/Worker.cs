using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Service.Services;

namespace ProjectSyndraBackend.Service;

public class Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var taskServices = scope.ServiceProvider.GetServices<ITaskService>().ToList();
        var tasks = new List<Task>();

        foreach (var taskService in taskServices)
        {
            tasks.Add(ScheduleTask(taskService, stoppingToken));
        }

        await Task.WhenAll(tasks);
    }
    private async Task ScheduleTask(ITaskService taskService, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await taskService.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown, log if necessary or handle gracefully
                break; // Exit the loop to end the task
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error executing task service {TaskServiceName}", taskService.GetType().Name);
            }

            // Respect the cancellation token by passing it to Task.Delay
            try
            {
                await Task.Delay(taskService.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Handle the cancellation gracefully
                break; // Exit the loop to end the task
            }
        }
    }
}