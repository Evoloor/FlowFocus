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
public abstract class BasePlannerService(
    ITaskRepository taskRepository,
    IDependencyRepository dependencyRepository,
    ISettingsRepository settingsRepository)
    : IPlannerService
{
    protected readonly ITaskRepository TaskRepository = taskRepository;
    protected readonly ISettingsRepository SettingsRepository = settingsRepository;

    public abstract Task<IEnumerable<TaskItem>> PlanTasksForDayAsync(DateTime date);
    public abstract Task RecalculatePrioritiesAsync();
    
    public virtual async Task<bool> ValidateDependenciesAsync(Guid taskId)
    {
        return !await HasCircularDependency(taskId, []);
    }
    
    public virtual async Task<double> CalculateDailyLoadAsync(DateTime date)
    {
        var tasks = await TaskRepository.GetByDateAsync(date);
        return tasks.Sum(t => t.EstimatedHours);
    }
    
    private async Task<bool> HasCircularDependency(Guid taskId, HashSet<Guid> visited)
    {
        if (!visited.Add(taskId))
            return true;

        var dependencies = await dependencyRepository.GetDependenciesForTaskAsync(taskId);
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
        var allTasks = await TaskRepository.GetAllAsync();
        var settings = await SettingsRepository.GetUserSettingsAsync();
        
        var availableTasks = allTasks
            .Where(t => t.Status is TaskStatus.Planned or TaskStatus.NotConfigured)
            .Where(t => t.Dependencies.All(d => d.Type != DependencyType.Blocking) || 
                        t.Dependencies.All(d => d.TargetTask?.Status == TaskStatus.Completed))
            .OrderByDescending(t => t.UserPriority)
            .ThenBy(t => t.Complexity)
            .ThenByDescending(t => t.Interest);

        var result = new List<TaskItem>();
        double totalTime = 0;
        var totalComplexity = 0;

        foreach (var task in availableTasks)
        {
            if (!(totalTime + task.EstimatedHours <= settings.DailyTimeLimit) ||
                totalComplexity + task.Complexity > settings.DailyComplexityLimit) continue;
            result.Add(task);
            totalTime += task.EstimatedHours;
            totalComplexity += task.Complexity;
        }

        return result;
    }

    public override async Task RecalculatePrioritiesAsync()
    {
        var tasks = await TaskRepository.GetAllAsync();
        foreach (var task in tasks)
        {
            // Базовая формула пересчета приоритетов
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

            // Учет блокировок
            var blockingDependencies = task.Dependencies
                .Where(d => d.Type == DependencyType.Blocking)
                .ToList();
                
            if (blockingDependencies.Count != 0)
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
            await TaskRepository.UpdateAsync(task);
        }
    }
}