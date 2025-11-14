using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;
namespace FlowFocus.Core;
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(Guid id);
    Task<bool> ExistsAsync(Guid id);
}

public interface ITaskRepository : IRepository<TaskItem>
{
    Task<IEnumerable<TaskItem>> GetByStatusAsync(TaskStatus status);
    Task<IEnumerable<TaskItem>> GetByDateAsync(DateTime date);
    Task<IEnumerable<TaskItem>> GetByPriorityRangeAsync(int minPriority, int maxPriority);
    Task<IEnumerable<TaskItem>> GetWithDependenciesAsync();
    Task<IEnumerable<TaskItem>> GetNotConfiguredAsync();
    Task UpdateStatusAsync(Guid taskId, TaskStatus status);
}
public interface IDependencyRepository : IRepository<Dependency>
{
    Task<IEnumerable<Dependency>> GetDependenciesForTaskAsync(Guid taskId);
    Task<IEnumerable<Dependency>> GetDependentTasksAsync(Guid taskId);
    Task<bool> HasCircularDependencyAsync(Guid sourceTaskId, Guid targetTaskId);
    Task RemoveDependenciesForTaskAsync(Guid taskId);
}
public interface ISettingsRepository : IRepository<UserSettings>
{
    Task<UserSettings> GetUserSettingsAsync();
}
public interface IPlannerService
{
    Task<IEnumerable<TaskItem>> PlanTasksForDayAsync(DateTime date);
    Task RecalculatePrioritiesAsync();
    Task<bool> ValidateDependenciesAsync(Guid taskId);
    Task<double> CalculateDailyLoadAsync(DateTime date);
}
public abstract class BasePlannerService : IPlannerService
{
    protected readonly ITaskRepository _taskRepository;
    protected readonly IDependencyRepository _dependencyRepository;
    protected readonly ISettingsRepository _settingsRepository;

    protected BasePlannerService(
        ITaskRepository taskRepository,
        IDependencyRepository dependencyRepository,
        ISettingsRepository settingsRepository)
    {
        _taskRepository = taskRepository;
        _dependencyRepository = dependencyRepository;
        _settingsRepository = settingsRepository;
    }

    public abstract Task<IEnumerable<TaskItem>> PlanTasksForDayAsync(DateTime date);
    public abstract Task RecalculatePrioritiesAsync();
    
    public virtual async Task<bool> ValidateDependenciesAsync(Guid taskId)
    {
        var dependencies = await _dependencyRepository.GetDependenciesForTaskAsync(taskId);
        return !await HasCircularDependency(taskId, new HashSet<Guid>());
    }
    
    public virtual async Task<double> CalculateDailyLoadAsync(DateTime date)
    {
        var tasks = await _taskRepository.GetByDateAsync(date);
        return tasks.Sum(t => t.EstimatedHours);
    }
    
    private async Task<bool> HasCircularDependency(Guid taskId, HashSet<Guid> visited)
    {
        if (visited.Contains(taskId))
            return true;
            
        visited.Add(taskId);
        
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

public class BasicPlannerService : BasePlannerService
{
    public BasicPlannerService(
        ITaskRepository taskRepository,
        IDependencyRepository dependencyRepository,
        ISettingsRepository settingsRepository)
        : base(taskRepository, dependencyRepository, settingsRepository)
    {
    }

    public override async Task<IEnumerable<TaskItem>> PlanTasksForDayAsync(DateTime date)
    {
        var allTasks = await _taskRepository.GetAllAsync();
        var settings = await _settingsRepository.GetUserSettingsAsync();
        
        var availableTasks = allTasks
            .Where(t => t.Status == TaskStatus.Planned || t.Status == TaskStatus.NotConfigured)
            .Where(t => !t.Dependencies.Any(d => d.Type == Enums.DependencyType.Blocking) || 
                       t.Dependencies.All(d => d.TargetTask?.Status == TaskStatus.Completed))
            .OrderByDescending(t => t.UserPriority)
            .ThenBy(t => t.Complexity)
            .ThenByDescending(t => t.Interest);

        var result = new List<TaskItem>();
        double totalTime = 0;
        int totalComplexity = 0;

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
            // Базовая формула пересчета приоритетов
            var calculatedPriority = task.UserPriority;
            
            // Учет дедлайна
            if (task.Deadline.HasValue)
            {
                var daysUntilDeadline = (task.Deadline.Value - DateTime.Today).TotalDays;
                if (daysUntilDeadline <= 1)
                    calculatedPriority = Math.Min(calculatedPriority, 1);
                else if (daysUntilDeadline <= 3)
                    calculatedPriority = Math.Min(calculatedPriority, 3);
                else if (daysUntilDeadline <= 7)
                    calculatedPriority = Math.Min(calculatedPriority, 5);
            }

            // Учет блокировок
            var blockingDependencies = task.Dependencies
                .Where(d => d.Type == Enums.DependencyType.Blocking)
                .ToList();
                
            if (blockingDependencies.Any())
            {
                var completedBlockers = blockingDependencies
                    .Count(d => d.TargetTask?.Status == TaskStatus.Completed);
                    
                if (completedBlockers == blockingDependencies.Count)
                {
                    // Все блокеры выполнены - повышаем приоритет
                    calculatedPriority = Math.Max(1, calculatedPriority - 2);
                }
                else
                {
                    // Есть невыполненные блокеры - понижаем приоритет
                    calculatedPriority += 5;
                }
            }

            task.CalculatedPriority = calculatedPriority;
            await _taskRepository.UpdateAsync(task);
        }
    }
}