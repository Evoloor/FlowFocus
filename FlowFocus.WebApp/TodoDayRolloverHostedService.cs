using FlowFocus.Core;

namespace FlowFocus.WebApp;

/// <summary>
/// Фоновый сервис, запускающий актуализацию приоритетов при наступлении нового логического дня (<see cref="TodoDay"/>).
/// </summary>
public sealed class TodoDayRolloverHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<TodoDayRolloverHostedService> logger) : BackgroundService
{
    private TodoDay _lastActualizedDay = TodoDay.Today;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var currentDay = TodoDay.Today;
            if (currentDay == _lastActualizedDay)
            {
                continue;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var plannerService = scope.ServiceProvider.GetRequiredService<IPlannerService>();
                plannerService.ActualizePriorities();
                _lastActualizedDay = currentDay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при актуализации приоритетов на новый Todo-день {TodoDay}", currentDay);
            }
        }
    }
}
