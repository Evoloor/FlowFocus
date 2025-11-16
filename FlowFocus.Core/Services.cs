using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core;

public interface IRepository<T> where T : class
{
    List<T> GetAll();
    T? GetById(int id);
    void Add(T entity);
    void Update(T entity);
    void Delete(int id);
    void SaveChanges();
}

public interface ITaskRepository : IRepository<TaskItem>
{
    List<TaskItem> GetByStatus(TaskStatus status);
    List<TaskItem> GetByDate(DateTime date);
    List<TaskItem> GetByPriorityRange(int minPriority, int maxPriority);
    List<TaskItem> GetWithDependencies();
    List<TaskItem> GetNotConfigured();
    List<TaskItem> GetRecurringTasks();
    List<TaskItem> GetChildTasks(int parentTaskId);
    void UpdateStatus(int taskId, TaskStatus status);
    void ProcessDayStart();
}

public interface IDependencyRepository : IRepository<Dependency>
{
    List<Dependency> GetDependenciesForTask(int taskId);
    List<Dependency> GetDependentTasks(int taskId);
    bool HasCircularDependency(int sourceTaskId, int targetTaskId);
    void RemoveDependenciesForTask(int taskId);
    List<Dependency> GetBlockingDependencies(int taskId);
}

public interface ISettingsRepository : IRepository<UserSettings>
{
    UserSettings GetUserSettings();
    bool GetAutoRecalculateSetting();
    void UpdateDayStartHour(int hour);
}

public interface IPlannerService
{
    List<TaskItem> PlanTasksForDay(DateTime date);
    void RecalculatePriorities();
    bool ValidateDependencies(int taskId);
    double CalculateDailyLoad(DateTime date);
    void BalanceTaskLoad(DateTime date);
}

public interface INotificationService
{
    void CheckDailyWarnings();
    void CheckTimeShortage(DateTime date);
    void CheckBlockedTasks();
    List<string> GetNotifications();
}

public interface IRecurringTaskService
{
    void ProcessRecurringTasks();
    void CreateNextRecurrence(TaskItem task);
}

public class NotificationService : INotificationService
{
    private readonly ITaskRepository _taskRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly List<string> _notifications = new();

    public NotificationService(ITaskRepository taskRepository, IDependencyRepository dependencyRepository, ISettingsRepository settingsRepository)
    {
        _taskRepository = taskRepository;
        _dependencyRepository = dependencyRepository;
        _settingsRepository = settingsRepository;
    }

    public void CheckDailyWarnings()
    {
        _notifications.Clear();
        var settings = _settingsRepository.GetUserSettings();
        var currentTime = DateTime.Now;
        var dayStart = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, settings.DayStartHour, 0, 0);
        var dayEnd = dayStart.AddHours(24);

        if (currentTime > dayEnd.AddHours(-2) && currentTime < dayEnd)
        {
            _notifications.Add("До конца дня осталось менее 2 часов!");
        }

        if (currentTime.TimeOfDay < TimeSpan.FromHours(settings.DayStartHour + 2))
        {
            var todayTasks = _taskRepository.GetByDate(DateTime.Today);
            var highPriorityTasks = todayTasks.Where(t => t.UserPriority <= 3).ToList();

            if (highPriorityTasks.Any())
            {
                _notifications.Add($"Сегодня {highPriorityTasks.Count} задач высокого приоритета");
            }
        }
    }

    public void CheckTimeShortage(DateTime date)
    {
        var tasks = _taskRepository.GetByDate(date);
        var activeTasks = tasks.Where(t => t.Status != TaskStatus.Completed).ToList();
        var totalTime = activeTasks.Sum(t => t.EstimatedHours);
        var settings = _settingsRepository.GetUserSettings();

        if (totalTime > settings.DailyTimeLimit * 1.2)
        {
            _notifications.Add($"Превышен дневной лимит времени на {totalTime - settings.DailyTimeLimit:0.0} часов");
        }

        var totalComplexity = activeTasks.Sum(t => t.Complexity);
        if (totalComplexity > settings.DailyComplexityLimit * 1.2)
        {
            _notifications.Add($"Превышен дневной лимит сложности на {totalComplexity - settings.DailyComplexityLimit} единиц");
        }
    }

    public void CheckBlockedTasks()
    {
        var tasks = _taskRepository.GetAll();
        var blockedTasks = tasks.Where(t =>
            t.Status == TaskStatus.Planned &&
            !t.CanBeStarted()
        ).ToList();

        if (blockedTasks.Any())
        {
            _notifications.Add($"Найдено {blockedTasks.Count} заблокированных задач");
        }
    }

    public List<string> GetNotifications()
    {
        CheckDailyWarnings();
        CheckTimeShortage(DateTime.Today);
        CheckBlockedTasks();
        return _notifications.ToList();
    }
}

public abstract class BasePlannerService : IPlannerService
{
    protected readonly ITaskRepository _taskRepository;
    protected readonly IDependencyRepository _dependencyRepository;
    protected readonly ISettingsRepository _settingsRepository;

    protected BasePlannerService(ITaskRepository taskRepository, IDependencyRepository dependencyRepository, ISettingsRepository settingsRepository)
    {
        _taskRepository = taskRepository;
        _dependencyRepository = dependencyRepository;
        _settingsRepository = settingsRepository;
    }

    public abstract List<TaskItem> PlanTasksForDay(DateTime date);
    public abstract void RecalculatePriorities();

    public virtual bool ValidateDependencies(int taskId)
    {
        return !HasCircularDependency(taskId, new HashSet<int>());
    }

    public virtual double CalculateDailyLoad(DateTime date)
    {
        var tasks = _taskRepository.GetByDate(date);
        return tasks.Where(t => t.Status != TaskStatus.Completed)
            .Sum(t => t.EstimatedHours);
    }

    public virtual void BalanceTaskLoad(DateTime date)
    {
        var tasks = _taskRepository.GetByDate(date).ToList();
        var settings = _settingsRepository.GetUserSettings();

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
                _taskRepository.Update(task);
            }
            _taskRepository.SaveChanges();
        }
    }

    private bool HasCircularDependency(int taskId, HashSet<int> visited)
    {
        if (!visited.Add(taskId))
            return true;

        var dependencies = _dependencyRepository.GetDependenciesForTask(taskId);
        foreach (var dependency in dependencies.Where(d => d.Type == DependencyType.Blocking))
        {
            if (HasCircularDependency(dependency.TargetTaskId, visited))
                return true;
        }

        visited.Remove(taskId);
        return false;
    }
}

public class BasicPlannerService : BasePlannerService
{
    public BasicPlannerService(ITaskRepository taskRepository, IDependencyRepository dependencyRepository, ISettingsRepository settingsRepository)
        : base(taskRepository, dependencyRepository, settingsRepository)
    {
    }

    public override List<TaskItem> PlanTasksForDay(DateTime date)
    {
        var allTasks = _taskRepository.GetAll();
        var settings = _settingsRepository.GetUserSettings();

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

    public override void RecalculatePriorities()
    {
        var tasks = _taskRepository.GetAll();
        var settings = _settingsRepository.GetUserSettings();

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
            _taskRepository.Update(task);
        }
        _taskRepository.SaveChanges();
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

        var balanceFactor = task.Interest / 10.0 * (1 - task.Complexity / 100.0);
        score *= 1 + balanceFactor * 0.3;

        if (task.IsFavorite) score *= 1.1;

        return score;
    }
}

public interface IProcrastinationService
{
    bool CanProcrastinateTask(int taskId);
    void ProcrastinateTask(int taskId);
    List<TaskItem> GetProcrastinationCandidates();
}

public class ProcrastinationService : IProcrastinationService
{
    private readonly ITaskRepository _taskRepository;
    private readonly ISettingsRepository _settingsRepository;

    public ProcrastinationService(ITaskRepository taskRepository, ISettingsRepository settingsRepository)
    {
        _taskRepository = taskRepository;
        _settingsRepository = settingsRepository;
    }

    public bool CanProcrastinateTask(int taskId)
    {
        var task = _taskRepository.GetById(taskId);
        var settings = _settingsRepository.GetUserSettings();
        
        return task != null && 
               task.CanBeProcrastinated() &&
               settings.ShowProcrastinationButton;
    }

    public void ProcrastinateTask(int taskId)
    {
        var task = _taskRepository.GetById(taskId);
        if (task == null) return;

        task.ProcrastinationCount++;
        task.LastProcrastinatedDate = DateTime.UtcNow;
        task.PlannedDate = DateTime.Today.AddDays(1);
        
        _taskRepository.Update(task);
        _taskRepository.SaveChanges();
    }

    public List<TaskItem> GetProcrastinationCandidates()
    {
        var tasks = _taskRepository.GetAll();
        return tasks.Where(t => t.CanBeProcrastinated()).ToList();
    }
}
public class RecurringTaskService(ITaskRepository taskRepository) : IRecurringTaskService
{
    public void ProcessRecurringTasks()
    {
        var recurringTasks = taskRepository.GetRecurringTasks();
        var today = DateTime.Today;

        foreach (var task in recurringTasks.Where(t => t.Recurrence != null))
        {
            if (ShouldCreateRecurrence(task, today))
            {
                CreateNextRecurrence(task);
            }
        }
    }

    public void CreateNextRecurrence(TaskItem task)
    {
        if (task.Recurrence == null) return;

        var nextDate = CalculateNextDate(task.Recurrence, DateTime.Today);
        if (nextDate.HasValue && (!task.RecurrenceEndDate.HasValue || nextDate <= task.RecurrenceEndDate))
        {
            var newTask = new TaskItem
            {
                Title = task.Title,
                Description = task.Description,
                UserPriority = task.UserPriority,
                Status = TaskStatus.Planned,
                Interest = task.Interest,
                Complexity = task.Complexity,
                EstimatedHours = task.EstimatedHours,
                PlannedDate = nextDate,
                IsRecurring = true,
                Recurrence = task.Recurrence,
                RecurrenceEndDate = task.RecurrenceEndDate,
                ParentTaskId = task.Id,
                Tags = new List<string>(task.Tags),
                DisplayType = task.DisplayType
            };

            taskRepository.Add(newTask);
            taskRepository.SaveChanges();
        }
    }

    private bool ShouldCreateRecurrence(TaskItem task, DateTime today)
    {
        if (task.Recurrence == null) return false;

        var lastOccurrence = task.PlannedDate ?? task.CreatedDate.Date;
        var nextDate = CalculateNextDate(task.Recurrence, lastOccurrence);

        return nextDate.HasValue && nextDate.Value.Date <= today.Date;
    }

    private DateTime? CalculateNextDate(RecurrencePattern pattern, DateTime lastDate)
    {
        return pattern.Type switch
        {
            RecurrenceType.Daily => lastDate.AddDays(pattern.Interval),
            RecurrenceType.Weekly => CalculateNextWeeklyDate(pattern, lastDate),
            RecurrenceType.Monthly => CalculateNextMonthlyDate(pattern, lastDate),
            RecurrenceType.Yearly => lastDate.AddYears(pattern.Interval),
            _ => null
        };
    }

    private DateTime? CalculateNextWeeklyDate(RecurrencePattern pattern, DateTime lastDate)
    {
        var nextDate = lastDate.AddDays(7 * pattern.Interval);
        if (pattern.DaysOfWeek?.Any() == true)
        {
            for (var i = 0; i < 7; i++)
            {
                var candidate = nextDate.AddDays(i);
                if (pattern.DaysOfWeek.Contains(candidate.DayOfWeek))
                    return candidate;
            }
        }

        return nextDate;
    }

    private DateTime? CalculateNextMonthlyDate(RecurrencePattern pattern, DateTime lastDate)
    {
        var nextDate = lastDate.AddMonths(pattern.Interval);
        if (pattern.DayOfMonth.HasValue)
        {
            var daysInMonth = DateTime.DaysInMonth(nextDate.Year, nextDate.Month);
            var day = Math.Min(pattern.DayOfMonth.Value, daysInMonth);
            return new DateTime(nextDate.Year, nextDate.Month, day);
        }

        return nextDate;
    }
}