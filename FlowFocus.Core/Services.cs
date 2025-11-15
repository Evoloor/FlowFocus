using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
}

public interface ITaskRepository : IRepository<TaskItem>
{
    Task<IEnumerable<TaskItem>> GetByStatusAsync(TaskStatus status);
    Task<IEnumerable<TaskItem>> GetByDateAsync(DateTime date);
    Task<IEnumerable<TaskItem>> GetByPriorityRangeAsync(int minPriority, int maxPriority);
    Task<IEnumerable<TaskItem>> GetWithDependenciesAsync();
    Task<IEnumerable<TaskItem>> GetNotConfiguredAsync();
    Task<IEnumerable<TaskItem>> GetRecurringTasksAsync();
    Task<IEnumerable<TaskItem>> GetChildTasksAsync(int parentTaskId);
    Task UpdateStatusAsync(int taskId, TaskStatus status);
    Task ProcessDayStartAsync();
}

public interface IDependencyRepository : IRepository<Dependency>
{
    Task<IEnumerable<Dependency>> GetDependenciesForTaskAsync(int taskId);
    Task<IEnumerable<Dependency>> GetDependentTasksAsync(int taskId);
    Task<bool> HasCircularDependencyAsync(int sourceTaskId, int targetTaskId);
    Task RemoveDependenciesForTaskAsync(int taskId);
    Task<IEnumerable<Dependency>> GetBlockingDependenciesAsync(int taskId);
}

public interface ISettingsRepository : IRepository<UserSettings>
{
    Task<UserSettings> GetUserSettingsAsync();
    Task<bool> GetAutoRecalculateSettingAsync();
    Task UpdateDayStartHourAsync(int hour);
}

public interface IPlannerService
{
    Task<IEnumerable<TaskItem>> PlanTasksForDayAsync(DateTime date);
    Task RecalculatePrioritiesAsync();
    Task<bool> ValidateDependenciesAsync(int taskId);
    Task<double> CalculateDailyLoadAsync(DateTime date);
    Task BalanceTaskLoadAsync(DateTime date); // Добавлен отсутствующий метод
}

public interface INotificationService
{
    Task CheckDailyWarningsAsync();
    Task CheckTimeShortageAsync(DateTime date);
    Task CheckBlockedTasksAsync();
    Task<List<string>> GetNotificationsAsync(); // Исправлен тип возвращаемого значения
}

public interface IRecurringTaskService
{
    Task ProcessRecurringTasksAsync();
    Task CreateNextRecurrenceAsync(TaskItem task);
}

public class NotificationService(
    ITaskRepository taskRepository,
    IDependencyRepository dependencyRepository,
    ISettingsRepository settingsRepository)
    : INotificationService
{
    private readonly IDependencyRepository _dependencyRepository = dependencyRepository;
    private readonly List<string> _notifications = [];

    public async Task CheckDailyWarningsAsync()
    {
        _notifications.Clear();
        var settings = await settingsRepository.GetUserSettingsAsync();
        var currentTime = DateTime.Now;
        var dayStart = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, settings.DayStartHour, 0, 0);
        var dayEnd = dayStart.AddHours(24);

        if (currentTime > dayEnd.AddHours(-2) && currentTime < dayEnd)
        {
            _notifications.Add("До конца дня осталось менее 2 часов!");
        }

        if (currentTime.TimeOfDay < TimeSpan.FromHours(settings.DayStartHour + 2))
        {
            var todayTasks = await taskRepository.GetByDateAsync(DateTime.Today);
            var highPriorityTasks = todayTasks.Where(t => t.UserPriority <= 3).ToList();

            if (highPriorityTasks.Any())
            {
                _notifications.Add($"Сегодня {highPriorityTasks.Count} задач высокого приоритета");
            }
        }
    }

    public async Task CheckTimeShortageAsync(DateTime date)
    {
        var tasks = await taskRepository.GetByDateAsync(date);
        var activeTasks = tasks.Where(t => t.Status != TaskStatus.Completed).ToList();
        var totalTime = activeTasks.Sum(t => t.EstimatedHours);
        var settings = await settingsRepository.GetUserSettingsAsync();

        if (totalTime > settings.DailyTimeLimit * 1.2)
        {
            _notifications.Add($"Превышен дневной лимит времени на {(totalTime - settings.DailyTimeLimit):0.0} часов");
        }

        var totalComplexity = activeTasks.Sum(t => t.Complexity);
        if (totalComplexity > settings.DailyComplexityLimit * 1.2)
        {
            _notifications.Add(
                $"Превышен дневной лимит сложности на {totalComplexity - settings.DailyComplexityLimit} единиц");
        }
    }

    public async Task CheckBlockedTasksAsync()
    {
        var tasks = await taskRepository.GetAllAsync();
        var blockedTasks = tasks.Where(t =>
            t.Status == TaskStatus.Planned &&
            !t.CanBeStarted()
        ).ToList();

        if (blockedTasks.Any())
        {
            _notifications.Add($"Найдено {blockedTasks.Count} заблокированных задач");
        }
    }

    public async Task<List<string>> GetNotificationsAsync()
    {
        await CheckDailyWarningsAsync();
        await CheckTimeShortageAsync(DateTime.Today);
        await CheckBlockedTasksAsync();

        return _notifications;
    }
}
public abstract class BasePlannerService(
    ITaskRepository taskRepository,
    IDependencyRepository dependencyRepository,
    ISettingsRepository settingsRepository)
    : IPlannerService
{
    protected readonly ITaskRepository _taskRepository = taskRepository;
    protected readonly IDependencyRepository _dependencyRepository = dependencyRepository;
    protected readonly ISettingsRepository _settingsRepository = settingsRepository;

    public abstract Task<IEnumerable<TaskItem>> PlanTasksForDayAsync(DateTime date);
    public abstract Task RecalculatePrioritiesAsync();

    public virtual async Task<bool> ValidateDependenciesAsync(int taskId)
    {
        return !await HasCircularDependency(taskId, []);
    }

    public virtual async Task<double> CalculateDailyLoadAsync(DateTime date)
    {
        var tasks = await _taskRepository.GetByDateAsync(date);
        return tasks.Where(t => t.Status != TaskStatus.Completed)
            .Sum(t => t.EstimatedHours);
    }

    public virtual async Task BalanceTaskLoadAsync(DateTime date)
    {
        var tasks = (await _taskRepository.GetByDateAsync(date)).ToList();
        var settings = await _settingsRepository.GetUserSettingsAsync();

        var complexTasks = tasks.Where(t => t.Complexity >= 70).ToList();

        // Балансировка: не более 30% сложных задач в день
        var maxComplexTasks = (int)Math.Ceiling(tasks.Count * 0.3);
        if (complexTasks.Count > maxComplexTasks)
        {
            var tasksToMove = complexTasks.OrderBy(t => t.UserPriority)
                .Take(complexTasks.Count - maxComplexTasks);

            foreach (var task in tasksToMove)
            {
                task.PlannedDate = date.AddDays(1);
                await _taskRepository.UpdateAsync(task);
            }
        }
    }

    private async Task<bool> HasCircularDependency(int taskId, HashSet<int> visited)
    {
        if (!visited.Add(taskId))
            return true;

        var dependencies = await _dependencyRepository.GetDependenciesForTaskAsync(taskId);
        foreach (var dependency in dependencies.Where(d => d.Type == DependencyType.Blocking))
        {
            if (await HasCircularDependency(dependency.TargetTaskId, visited))
                return true;
        }

        visited.Remove(taskId);
        return false;
    }
}

public class BasicPlannerService(
    ITaskRepository taskRepository,
    IDependencyRepository dependencyRepository,
    ISettingsRepository settingsRepository)
    : BasePlannerService(taskRepository, dependencyRepository, settingsRepository)
{
    public override async Task<IEnumerable<TaskItem>> PlanTasksForDayAsync(DateTime date)
    {
        var allTasks = await _taskRepository.GetAllAsync();
        var settings = await _settingsRepository.GetUserSettingsAsync();

        var availableTasks = allTasks
            .Where(t => t.Status is TaskStatus.Planned or TaskStatus.NotConfigured)
            .Where(t => t.CanBeStarted())
            .OrderByDescending(t => GetTaskScore(t, date))
            .ThenBy(t => t.Complexity)
            .ThenByDescending(t => t.Interest);

        var result = new List<TaskItem>();
        double totalTime = 0;
        var totalComplexity = 0;

        foreach (var task in availableTasks)
        {
            if (totalTime + task.EstimatedHours <= settings.DailyTimeLimit &&
                totalComplexity + task.Complexity <= settings.DailyComplexityLimit)
            {
                result.Add(task);
                totalTime += task.EstimatedHours;
                totalComplexity += task.Complexity;
            }
        }

        return result;
    }

    public override async Task RecalculatePrioritiesAsync()
    {
        var tasks = await _taskRepository.GetAllAsync();
        var settings = await _settingsRepository.GetUserSettingsAsync();

        foreach (var task in tasks)
        {
            var calculatedPriority = task.UserPriority;

            // Учет дедлайна
            if (task.Deadline.HasValue)
            {
                var daysUntilDeadline = (task.Deadline.Value - DateTime.Today).TotalDays;
                calculatedPriority = daysUntilDeadline switch
                {
                    <= 1 => Math.Min(calculatedPriority, 1),
                    <= 3 => Math.Min(calculatedPriority, 3),
                    <= 7 => Math.Min(calculatedPriority, 5),
                    _ => calculatedPriority
                };
            }

            // Учет специальных дат повышения приоритета
            if (!string.IsNullOrEmpty(settings.PriorityBoostDates))
            {
                var boostDates = settings.PriorityBoostDates.Split(',')
                    .Select(d => d.Trim())
                    .Where(d => DateTime.TryParseExact(d, "dd.MM", null, System.Globalization.DateTimeStyles.None,
                        out _))
                    .ToList();

                var todayString = DateTime.Today.ToString("dd.MM");
                if (boostDates.Contains(todayString) && calculatedPriority > 5)
                {
                    calculatedPriority = 5;
                }
            }

            // Учет блокировок
            var blockingDependencies = task.Dependencies
                .Where(d => d.Type == DependencyType.Blocking)
                .ToList();

            if (blockingDependencies.Count > 0)
            {
                var completedBlockers = blockingDependencies
                    .Count(d => d.TargetTask?.Status == TaskStatus.Completed);

                if (completedBlockers == blockingDependencies.Count)
                {
                    calculatedPriority = Math.Max(1, calculatedPriority - 2);
                }
                else
                {
                    calculatedPriority += 5;
                }
            }

            task.CalculatedPriority = calculatedPriority;
            await _taskRepository.UpdateAsync(task);
        }
    }

    private double GetTaskScore(TaskItem task, DateTime date)
    {
        var score = (double)task.CalculatedPriority;

        if (task.Deadline.HasValue)
        {
            var daysUntilDeadline = (task.Deadline.Value - date).TotalDays;
            if (daysUntilDeadline <= 1) score *= 2;
            else if (daysUntilDeadline <= 3) score *= 1.5;
        }

        var balanceFactor = (task.Interest / 10.0) * (1 - task.Complexity / 100.0);
        score *= (1 + balanceFactor * 0.3);

        if (task.IsFavorite) score *= 1.1;

        return score;
    }
}
