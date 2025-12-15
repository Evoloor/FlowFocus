using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data;

/// <summary>
/// Репозиторий задач
/// </summary>
public class TaskRepository(StorageContext context) : CachedRepository<TaskItem>(context), ITaskRepository
{
    protected override DbSet<TaskItem> GetDbSet() => Context.Tasks;

    protected override IQueryable<TaskItem> GetBaseQuery() =>
        GetDbSet()
            .AsNoTracking()
            .Include(t => t.Priority)
            .Include(t => t.EffectivePriority)
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.Relations).ThenInclude(r => r.TargetTask)
            .Include(t => t.InverseRelations).ThenInclude(r => r.SourceTask)
            .Include(t => t.Subtasks)
            .Include(t => t.PriorityEscalations).ThenInclude(pe => pe.TargetPriority)
            .AsQueryable();

    /// <summary>
    /// Получить задачи без подзадач (для списков)
    /// </summary>
    private IEnumerable<TaskItem> FilterOutSubtasks(IEnumerable<TaskItem> tasks)
    {
        return tasks.Where(t => t.ParentTaskId == null);
    }

    public List<TaskItem> GetTasksForDate(DateTime date)
    {
        var all = GetAll();
        return FilterOutSubtasks(all)
            .Where(t => t.ActualAssignedDate != null &&
                        t.ActualAssignedDate.Value.Date == date.Date &&
                        t.Status != TaskStatus.Completed &&
                        t.Status != TaskStatus.Irrelevant)
            .ToList();
    }

    public List<TaskItem> GetTodayTasks(int dayStartHour)
    {
        var logicalToday = DateHelper.GetLogicalToday(dayStartHour);
        var all = GetAll();
        return FilterOutSubtasks(all)
            .Where(t => t.ActualAssignedDate != null &&
                        t.ActualAssignedDate.Value.Date == logicalToday.Date &&
                        t.Status != TaskStatus.Completed &&
                        t.Status != TaskStatus.Irrelevant)
            .ToList();
    }

    public List<TaskItem> GetTomorrowTasks()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        return GetTasksForDate(tomorrow);
    }

    public List<TaskItem> GetNotConfiguredTasks()
    {
        var all = GetAll();
        return FilterOutSubtasks(all)
            .Where(t => t.Status == TaskStatus.NotConfigured)
            .ToList();
    }

    public int GetNotConfiguredCount()
    {
        // Исключаем подзадачи из подсчёта
        return GetAll().Count(t => t.ParentTaskId == null && t.Status == TaskStatus.NotConfigured);
    }

    public List<TaskItem> GetOverdueTasks(int dayStartHour)
    {
        var logicalToday = DateHelper.GetLogicalToday(dayStartHour);
        var all = GetAll();
        return FilterOutSubtasks(all)
            .Where(t => t.ActualAssignedDate != null &&
                        t.ActualAssignedDate.Value.Date < logicalToday.Date &&
                        t.Status != TaskStatus.Completed &&
                        t.Status != TaskStatus.Irrelevant)
            .ToList();
    }

    public List<TaskItem> GetTasksUnblockedBy(int taskId)
    {
        var all = GetAll();
        return all
            .Where(t => t.Relations.Any(r =>
                r.Type == RelationType.BlockedBy &&
                r.TargetTaskId == taskId))
            .Where(t =>
            {
                // Проверяем, что это был единственный активный блокер
                var otherBlockers = t.Relations
                    .Where(r => r.Type == RelationType.BlockedBy && r.TargetTaskId != taskId)
                    .Select(r => r.TargetTask)
                    .Where(blocker => blocker != null &&
                                      blocker.Status != TaskStatus.Completed &&
                                      blocker.Status != TaskStatus.Irrelevant);
                return !otherBlockers.Any();
            })
            .ToList();
    }

    public List<TaskItem> GetTasksForAutocomplete()
    {
        var all = GetAll();
        return FilterOutSubtasks(all)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant)
            .OrderBy(t => t.Title)
            .ToList();
    }

    public void CompleteTask(int taskId)
    {
        var task = GetById(taskId);
        if (task == null) return;

        UpdatePartial(taskId, t =>
        {
            t.Status = TaskStatus.Completed;
            t.CompletedDate = DateTime.UtcNow;
        });

        // Создаём следующую копию для повторяющихся задач
        if (task.IsRecurring && task.RecurrenceType != RecurrenceType.None)
        {
            CreateNextRecurrence(task);
        }
    }

    /// <summary>
    /// Создать следующую копию повторяющейся задачи
    /// </summary>
    private void CreateNextRecurrence(TaskItem sourceTask)
    {
        var nextDate = CalculateNextRecurrenceDate(sourceTask);
        if (nextDate == null) return;

        var newTask = new TaskItem
        {
            Title = sourceTask.Title,
            Description = sourceTask.Description,
            Status = TaskStatus.Planned,
            PriorityId = sourceTask.PriorityId,
            Interest = sourceTask.Interest,
            Complexity = sourceTask.Complexity,
            EstimatedMinutes = sourceTask.EstimatedMinutes,
            IsFavorite = sourceTask.IsFavorite,
            HideUnderSpoiler = sourceTask.HideUnderSpoiler,
            UserAssignedDate = nextDate,
            ActualAssignedDate = nextDate,
            IsRecurring = true,
            RecurrenceType = sourceTask.RecurrenceType,
            RecurrenceInterval = sourceTask.RecurrenceInterval,
            RecurrenceWeekDays = sourceTask.RecurrenceWeekDays,
            RecurrenceSourceId = sourceTask.RecurrenceSourceId ?? sourceTask.Id,
            CreatedDate = DateTime.UtcNow
        };

        Add(newTask);
    }

    /// <summary>
    /// Рассчитать следующую дату повторения
    /// </summary>
    private DateTime? CalculateNextRecurrenceDate(TaskItem task)
    {
        var completedDate = task.CompletedDate ?? DateTime.UtcNow;
        var baseDate = completedDate.Date;

        return task.RecurrenceType switch
        {
            RecurrenceType.Daily => baseDate.AddDays(1),
            RecurrenceType.EveryNDays => baseDate.AddDays(task.RecurrenceInterval ?? 1),
            RecurrenceType.WeekDays => CalculateNextWeekDayDate(baseDate, task.RecurrenceWeekDays ?? 0),
            _ => null
        };
    }

    /// <summary>
    /// Рассчитать следующую дату для повторения по дням недели
    /// </summary>
    private DateTime CalculateNextWeekDayDate(DateTime baseDate, int weekDaysMask)
    {
        var currentDay = (int)baseDate.DayOfWeek;
        // Конвертируем DayOfWeek (0=Sunday) в нашу маску (1=Monday)
        var maskDay = currentDay == 0 ? 64 : (1 << (currentDay - 1));

        // Ищем следующий день недели из маски
        for (int i = 1; i <= 7; i++)
        {
            var nextDay = (currentDay + i) % 7;
            var nextMaskDay = nextDay == 0 ? 64 : (1 << (nextDay - 1));
            
            if ((weekDaysMask & nextMaskDay) != 0)
            {
                return baseDate.AddDays(i);
            }
        }

        // Если не нашли, возвращаем через неделю
        return baseDate.AddDays(7);
    }

    public void MarkIrrelevant(int taskId)
    {
        UpdatePartial(taskId, task =>
        {
            task.Status = TaskStatus.Irrelevant;
        });
    }

    public TaskItem? GetProcrastinationTask(List<int> excludeIds)
    {
        var all = GetAll();
        return FilterOutSubtasks(all)
            .Where(t => t is { Interest: >= AppConfig.MinProcrastinationInterest, Status: TaskStatus.Planned } &&
                        !excludeIds.Contains(t.Id))
            .OrderByDescending(t => t.Interest - Math.Sqrt(t.EffectivePriority?.Order ?? t.Priority?.Order ?? 99))
            .FirstOrDefault();
    }

    public TaskItem? GetLeastPriorityTaskOfDay()
    {
        var today = DateTime.Today;
        var all = GetAll();
        var todayTasks = FilterOutSubtasks(all)
            .Where(t => t.ActualAssignedDate != null &&
                        t.ActualAssignedDate.Value.Date == today &&
                        t.Status != TaskStatus.Completed &&
                        t.Status != TaskStatus.Irrelevant);
        return todayTasks
            .Where(t => t.Status == TaskStatus.Planned)
            .OrderByDescending(t => t.EffectivePriority?.Order ?? t.Priority?.Order ?? 99)
            .ThenBy(t => t.Interest ?? 0)
            .FirstOrDefault();
    }
}

/// <summary>
/// Репозиторий настроек
/// </summary>
public class SettingsRepository(StorageContext context) : CachedRepository<UserSettings>(context), ISettingsRepository
{
    protected override DbSet<UserSettings> GetDbSet() => Context.Settings;

    public UserSettings GetUserSettings()
    {
        var settings = GetAll().FirstOrDefault();
        if (settings == null)
        {
            settings = new UserSettings { Id = 1 };
            Add(settings);
        }
        return settings;
    }

    public void UpdateSettings(UserSettings settings)
    {
        Update(settings);
    }
}

/// <summary>
/// Репозиторий приоритетов
/// </summary>
public class PriorityRepository(StorageContext context) : CachedRepository<PriorityLevel>(context), IPriorityRepository
{
    protected override DbSet<PriorityLevel> GetDbSet() => Context.Priorities;

    public List<PriorityLevel> GetAllOrdered()
    {
        return GetAll().OrderBy(p => p.Order).ToList();
    }

    public PriorityLevel? GetHighestPriority()
    {
        return GetAllOrdered().FirstOrDefault();
    }

    public List<PriorityLevel> GetPrioritiesHigherThan(int priorityId)
    {
        var priority = GetById(priorityId);
        if (priority == null) return GetAllOrdered();

        return GetAll()
            .Where(p => p.Order < priority.Order)
            .OrderBy(p => p.Order)
            .ToList();
    }

    public void Reorder(List<int> orderedIds)
    {
        lock (CacheLock)
        {
            var priorities = Context.Priorities.ToList();
            for (int i = 0; i < orderedIds.Count; i++)
            {
                var priority = priorities.FirstOrDefault(p => p.Id == orderedIds[i]);
                if (priority != null)
                {
                    priority.Order = i + 1;
                    priority.LastChangesOn = DateTime.UtcNow;
                }
            }
            Context.SaveChanges();
            MarkDirty();
        }
    }
}

/// <summary>
/// Репозиторий тегов
/// </summary>
public class TagRepository(StorageContext context) : CachedRepository<Tag>(context), ITagRepository
{
    protected override DbSet<Tag> GetDbSet() => Context.Tags;

    public Tag? GetByName(string name)
    {
        return GetAll().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public Tag GetOrCreate(string name)
    {
        var existing = GetByName(name);
        if (existing != null) return existing;

        var tag = new Tag
        {
            Name = name,
            BackgroundColor = GeneratePastelColor()
        };
        Add(tag);
        return tag;
    }

    public List<Tag> GetPopularTags(int count)
    {
        return GetAll()
            .OrderByDescending(t => t.UsageCount)
            .Take(count)
            .ToList();
    }

    public List<Tag> SearchByName(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return GetAll()
            .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.UsageCount)
            .Take(limit)
            .ToList();
    }

    public void IncrementUsage(int tagId)
    {
        UpdatePartial(tagId, tag =>
        {
            tag.UsageCount++;
            tag.LastUsedDate = DateTime.UtcNow;
        });
    }

    private static readonly string[] PastelColors =
    [
        "#FFB3BA", "#FFDFBA", "#FFFFBA", "#BAFFC9", "#BAE1FF",
        "#E0BBE4", "#FEC8D8", "#D4F0F0", "#CCE2CB", "#B6CFB6",
        "#97C1A9", "#FCB9AA", "#FFDBCC", "#ECEAE4", "#A2E1DB"
    ];

    private static string GeneratePastelColor()
    {
        return PastelColors[Random.Shared.Next(PastelColors.Length)];
    }
}

/// <summary>
/// Сессионный сервис тегов
/// </summary>
public class TagSessionService(ITagRepository tagRepository) : ITagSessionService
{
    private Tag? _lastUsedTag;

    public Tag? LastUsedTag => _lastUsedTag;

    public void MarkTagUsed(Tag tag)
    {
        _lastUsedTag = tag;
        tagRepository.IncrementUsage(tag.Id);
    }

    public List<Tag> GetSuggestedTags(int count = 5)
    {
        var result = new List<Tag>();

        if (_lastUsedTag != null)
        {
            result.Add(_lastUsedTag);
        }

        var popular = tagRepository.GetPopularTags(count);
        foreach (var tag in popular)
        {
            if (result.Count >= count) break;
            if (!result.Any(t => t.Id == tag.Id))
            {
                result.Add(tag);
            }
        }

        return result;
    }
}

