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
    
    public override void Update(TaskItem entity)
    {
        lock (CacheLock)
        {
            try
            {
                if (entity.Id <= 0)
                {
                    throw new InvalidOperationException(
                        $"Invalid entity ID: {entity.Id}. Entity must have a valid ID > 0 for update.");
                }

                var trackedEntity = GetTrackedQuery()
                    .Include(t => t.Tags)
                    .Include(t => t.Subtasks)
                    .Include(t => t.Relations)
                    .Include(t => t.PriorityEscalations)
                    .FirstOrDefault(e => e.Id == entity.Id);
                    
                if (trackedEntity == null)
                {
                    throw new InvalidOperationException($"Entity with ID {entity.Id} not found");
                }

                // Обновляем основные свойства
                Context.Entry(trackedEntity).CurrentValues.SetValues(entity);
                
                // Обновляем теги
                UpdateTags(trackedEntity, entity);
                
                // Обновляем подзадачи
                UpdateSubtasks(trackedEntity, entity);
                
                // Обновляем связи
                UpdateRelations(trackedEntity, entity);
                
                // Обновляем повышения приоритета
                UpdateEscalations(trackedEntity, entity);
                
                trackedEntity.LastChangesOn = DateTime.UtcNow;
                Context.SaveChanges();
                MarkDirty();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Update: {ex.Message}");
                throw;
            }
        }
    }
    
    private void UpdateTags(TaskItem tracked, TaskItem source)
    {
        // Получаем список ID тегов из source
        var sourceTagIds = source.Tags.Select(st => st.TagId).ToHashSet();
        var trackedTagIds = tracked.Tags.Select(tt => tt.TagId).ToHashSet();
        
        // Удаляем теги, которых нет в source
        var tagsToRemove = tracked.Tags.Where(tt => !sourceTagIds.Contains(tt.TagId)).ToList();
        var removedTagIds = tagsToRemove.Select(tt => tt.TagId).ToList();
        
        foreach (var tag in tagsToRemove)
        {
            Context.TaskTags.Remove(tag);
        }
        
        // Добавляем новые теги, которых нет в tracked
        foreach (var sourceTag in source.Tags)
        {
            if (!trackedTagIds.Contains(sourceTag.TagId))
            {
                Context.TaskTags.Add(new TaskTag
                {
                    TaskId = tracked.Id,
                    TagId = sourceTag.TagId
                });
            }
        }
        
        // Проверяем и удаляем неиспользуемые теги
        if (removedTagIds.Any())
        {
            var tagRepo = new TagRepository(Context);
            tagRepo.CleanupUnusedTags(removedTagIds);
        }
    }
    
    private void UpdateSubtasks(TaskItem tracked, TaskItem source)
    {
        // Удаляем удаленные подзадачи
        var subtasksToRemove = tracked.Subtasks.Where(st => !source.Subtasks.Any(sst => sst.Id == st.Id && sst.Id > 0)).ToList();
        foreach (var subtask in subtasksToRemove)
        {
            Context.Tasks.Remove(subtask);
        }
        
        // Обновляем существующие и добавляем новые подзадачи
        foreach (var sourceSubtask in source.Subtasks)
        {
            if (sourceSubtask.Id > 0)
            {
                var existing = tracked.Subtasks.FirstOrDefault(s => s.Id == sourceSubtask.Id);
                if (existing != null)
                {
                    Context.Entry(existing).CurrentValues.SetValues(sourceSubtask);
                }
            }
            else
            {
                sourceSubtask.ParentTaskId = tracked.Id;
                Context.Tasks.Add(sourceSubtask);
            }
        }
    }
    
    private void UpdateRelations(TaskItem tracked, TaskItem source)
    {
        // Удаляем старые связи
        var relationsToRemove = tracked.Relations.Where(r => !source.Relations.Any(sr => sr.Id == r.Id && sr.Id > 0)).ToList();
        foreach (var relation in relationsToRemove)
        {
            Context.TaskRelations.Remove(relation);
        }
        
        // Обновляем существующие и добавляем новые связи
        foreach (var sourceRelation in source.Relations)
        {
            if (sourceRelation.Id > 0)
            {
                var existing = tracked.Relations.FirstOrDefault(r => r.Id == sourceRelation.Id);
                if (existing != null)
                {
                    Context.Entry(existing).CurrentValues.SetValues(sourceRelation);
                }
            }
            else
            {
                sourceRelation.SourceTaskId = tracked.Id;
                Context.TaskRelations.Add(sourceRelation);
            }
        }
    }
    
    private void UpdateEscalations(TaskItem tracked, TaskItem source)
    {
        // Удаляем старые повышения
        var escalationsToRemove = tracked.PriorityEscalations.Where(e => !source.PriorityEscalations.Any(se => se.Id == e.Id && se.Id > 0)).ToList();
        foreach (var escalation in escalationsToRemove)
        {
            Context.PriorityEscalations.Remove(escalation);
        }
        
        // Обновляем существующие и добавляем новые повышения
        foreach (var sourceEscalation in source.PriorityEscalations)
        {
            if (sourceEscalation.Id > 0)
            {
                var existing = tracked.PriorityEscalations.FirstOrDefault(e => e.Id == sourceEscalation.Id);
                if (existing != null)
                {
                    Context.Entry(existing).CurrentValues.SetValues(sourceEscalation);
                }
            }
            else
            {
                sourceEscalation.TaskId = tracked.Id;
                Context.PriorityEscalations.Add(sourceEscalation);
            }
        }
    }

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
    
    public override void Delete(int id)
    {
        lock (CacheLock)
        {
            // Получаем задачу с тегами перед удалением
            var task = GetTrackedQuery()
                .Include(t => t.Tags)
                .FirstOrDefault(e => e.Id == id);
                
            if (task == null) return;
            
            // Сохраняем ID тегов для проверки после удаления
            var tagIds = task.Tags.Select(tt => tt.TagId).ToList();
            
            // Удаляем задачу
            GetDbSet().Remove(task);
            Context.SaveChanges();
            
            // Проверяем и удаляем неиспользуемые теги
            if (tagIds.Any())
            {
                var tagRepo = new TagRepository(Context);
                tagRepo.CleanupUnusedTags(tagIds);
            }
            
            MarkDirty();
        }
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
    
    public void CleanupUnusedTags(List<int> tagIds)
    {
        if (tagIds == null || !tagIds.Any()) return;
        
        lock (CacheLock)
        {
            foreach (var tagId in tagIds)
            {
                // Проверяем, есть ли еще ссылки на этот тег
                var hasReferences = Context.TaskTags.Any(tt => tt.TagId == tagId);
                
                if (!hasReferences)
                {
                    // Тег больше не используется, удаляем его
                    var tag = Context.Tags.Find(tagId);
                    if (tag != null)
                    {
                        Context.Tags.Remove(tag);
                    }
                }
            }
            
            Context.SaveChanges();
            MarkDirty();
        }
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

        // Сперва последний использованный тег из хипа (за сессию), если такой имеется
        if (_lastUsedTag != null)
        {
            result.Add(_lastUsedTag);
        }

        // Затем заполняем ещё 4 (или 5, если не было недавнего) по принципу самых используемых в созданных делах
        var remainingCount = count - result.Count;
        if (remainingCount > 0)
        {
            var popular = tagRepository.GetPopularTags(remainingCount + 5); // Берем больше, чтобы исключить уже добавленные
            foreach (var tag in popular.TakeWhile(tag => result.Count < count).Where(tag => !result.Any(t => t.Id == tag.Id)))
            {
                result.Add(tag);
            }
        }

        return result;
    }
}

