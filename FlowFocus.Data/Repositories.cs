using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data;

/// <summary>
/// Репозиторий задач
/// </summary>
public class TaskRepository(StorageContext context, INotificationService notificationService) : CachedRepository<TaskItem>(context, notificationService), ITaskRepository
{
    protected override DbSet<TaskItem> GetDbSet() => Context.Tasks;
    
    public override void Add(TaskItem entity)
    {
        lock (CacheLock)
        {
            if (entity.Id == 0)
            {
                entity.Id = GetNextId();
            }

            entity.LastChangesOn = DateTime.UtcNow;
            
            // Устанавливаем ParentTaskId для подзадач перед сохранением
            if (entity.Subtasks.Count != 0)
            {
                foreach (var subtask in entity.Subtasks)
                {
                    subtask.ParentTaskId ??= entity.Id;
                    if (subtask.CreatedDate == default)
                    {
                        subtask.CreatedDate = DateTime.UtcNow;
                    }
                    subtask.Status = TaskStatus.Planned;
                }
            }
            
            // Устанавливаем SourceTaskId для связей перед сохранением
            if (entity.Relations.Count != 0)
            {
                foreach (var relation in entity.Relations)
                {
                    if (relation.SourceTaskId == 0)
                    {
                        relation.SourceTaskId = entity.Id;
                    }
                    relation.LastChangesOn = DateTime.UtcNow;
                }
            }
            
            // Устанавливаем TaskId для повышений приоритета перед сохранением
            if (entity.PriorityEscalations.Count != 0)
            {
                foreach (var escalation in entity.PriorityEscalations)
                {
                    if (escalation.TaskId == 0)
                    {
                        escalation.TaskId = entity.Id;
                    }
                    escalation.LastChangesOn = DateTime.UtcNow;
                }
            }
            
            GetDbSet().Add(entity);
            Context.SaveChanges();
            MarkDirty();
        }
    }
    
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
        foreach (var sourceTag in source.Tags.Where(sourceTag => !trackedTagIds.Contains(sourceTag.TagId)))
        {
            Context.TaskTags.Add(new()
            {
                TaskId = tracked.Id,
                TagId = sourceTag.TagId
            });
        }
        
        // Проверяем и обновляем usageCount для удалённых тегов
        if (removedTagIds.Count != 0)
        {
            var tagRepo = new TagRepository(Context, NotificationService);
            // Для каждого удалённого тега уменьшаем usageCount и при необходимости удаляем сам тег
            foreach (var id in removedTagIds)
            {
                try
                {
                    tagRepo.DecrementUsage(id);
                }
                catch
                {
                    // Не фатально — продолжим
                }
            }
            try
            {
                // Дополнительная защита: удаляем теги без ссылок, если они остались
                tagRepo.CleanupUnusedTags(removedTagIds);
            }
            catch
            {
                // Не фатально
            }
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
        // Синхронизируем связи в БД с желаемым набором из source.Relations.
        // Желанные связи (canonical) могут содержать записи, где tracked является и Source, и Target.
        var desired = source.Relations ?? [];
        
        // Собираем все существующие записи, где tracked является Source или Target
        var trackedOutgoing = tracked.Relations ?? [];
        var trackedIncoming = tracked.InverseRelations ?? [];
        var trackedAll = trackedOutgoing.Concat(trackedIncoming).ToList();
        
        // Удаляем те существующие записи (outgoing или incoming), которых нет в desired
        var toRemove = trackedAll.Where(r =>
            // Если у записи есть Id, ищем по Id в desired
            (r.Id > 0 && !desired.Any(d => d.Id > 0 && d.Id == r.Id))
            // Или, если нет Id, пытаемся сверить по Source/Target/Type
            || !desired.Any(d => d.SourceTaskId == r.SourceTaskId && d.TargetTaskId == r.TargetTaskId && d.Type == r.Type)
        ).ToList();
        
        foreach (var rel in toRemove)
        {
            Context.TaskRelations.Remove(rel);
        }
        
        // Обновляем или добавляем желаемые записи
        foreach (var desiredRel in desired)
        {
            TaskRelation? existing = null;
            
            if (desiredRel.Id > 0)
            {
                existing = trackedAll.FirstOrDefault(r => r.Id == desiredRel.Id);
            }
            
            // Если не найдено по Id, ищем по Source/Target/Type
            if (existing == null)
            {
                existing = trackedAll.FirstOrDefault(r => r.SourceTaskId == desiredRel.SourceTaskId && r.TargetTaskId == desiredRel.TargetTaskId && r.Type == desiredRel.Type);
            }
            
            if (existing != null)
            {
                // Обновляем значения
                Context.Entry(existing).CurrentValues.SetValues(desiredRel);
            }
            else
            {
                // Если SourceTaskId не задан (0), и мы обновляем tracked (новая задача), устанавливаем Source = tracked.Id
                if (desiredRel.SourceTaskId == 0)
                {
                    desiredRel.SourceTaskId = tracked.Id;
                }
                Context.TaskRelations.Add(desiredRel);
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
            .Where(t =>
                // If another task blocks this task, it's represented as an inverse relation where SourceTask (blocker) has Type == Blocks
                t.InverseRelations.Any(r => r.Type == RelationType.Blocks && r.SourceTaskId == taskId)
            )
            .Where(t =>
            {
                // Собираем всех других блокеров (теперь мы учитываем только inverse relations с Type == Blocks)
                var otherInverseBlockers = t.InverseRelations
                    .Where(r => r.Type == RelationType.Blocks && r.SourceTaskId != taskId)
                    .Select(r => r.SourceTask)
                    .Where(blocker => blocker != null && blocker.Status != TaskStatus.Completed && blocker.Status != TaskStatus.Irrelevant)
                    .ToList();

                return !otherInverseBlockers.Any();
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

        // Для просроченных задач дата завершения должна быть равна дате назначения
        // Это важно для правильного расчета следующей даты повторения
        var completedDate = task.ActualAssignedDate ?? task.UserAssignedDate;
        if (completedDate != null && completedDate.Value.Date < DateTime.UtcNow.Date)
        {
            // Задача просрочена - используем дату назначения
            completedDate = completedDate.Value.Date;
        }
        else
        {
            // Задача не просрочена - используем текущую дату
            completedDate = DateTime.UtcNow;
        }

        UpdatePartial(taskId, t =>
        {
            t.Status = TaskStatus.Completed;
            t.CompletedDate = completedDate;
        });

        // Обновляем локальную копию даты завершения, чтобы CalculateNextRecurrenceDate использовал корректную базу
        task.CompletedDate = completedDate;

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
        try
        {
            var nextDate = CalculateNextRecurrenceDate(sourceTask);
            if (nextDate == null) return;

            // Защита от дублирования: если уже есть задача-реплика с тем же источником и датой, не создаём
            var sourceId = sourceTask.RecurrenceSourceId ?? sourceTask.Id;
            var start = nextDate.Value.Date;
            var end = start.AddDays(1);
            var exists = Context.Tasks
                .AsNoTracking()
                .Any(t => ((t.RecurrenceSourceId.HasValue && t.RecurrenceSourceId.Value == sourceId) || (!t.RecurrenceSourceId.HasValue && t.Id == sourceId))
                          && t.UserAssignedDate.HasValue && t.UserAssignedDate.Value >= start && t.UserAssignedDate.Value < end);
            if (exists) return;
             

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
        catch (Exception ex)
        {
            // Логируем и не даём упасть приложению
            Console.WriteLine($"CreateNextRecurrence error for task {sourceTask?.Id}: {ex}");
        }
    }

    /// <summary>
    /// Рассчитать следующую дату повторения
    /// </summary>
    private DateTime? CalculateNextRecurrenceDate(TaskItem task)
    {
        // completedDate — дата завершения, если установлена, иначе сейчас
        var completedDate = task.CompletedDate ?? DateTime.UtcNow;

        // Если задача имела дату назначения, и выполнение произошло раньше этой даты (т.е. досрочное выполнение),
        // используем дату назначения как базовую для расчёта следующей повторки. Это покрывает случай "выполнил задачу, назначенную на завтра",
        // когда базой для расчёта следующей даты должна быть исходная UserAssignedDate, а не дата выполнения.
        var assignedDate = task.UserAssignedDate ?? completedDate;

        // Базовая дата — максимум между датой выполнения и датой назначения (по дням)
        var baseDate = completedDate.Date >= assignedDate.Date ? completedDate.Date : assignedDate.Date;

        return task.RecurrenceType switch
        {
            RecurrenceType.Daily => baseDate.AddDays(1),
            RecurrenceType.EveryNDays => baseDate.AddDays(task.RecurrenceInterval ?? 1),
            RecurrenceType.WeekDays => CalculateNextWeekDayDate(baseDate, task.RecurrenceWeekDays ?? 0),
            RecurrenceType.Monthly => CalculateNextMonthDate(baseDate, task.RecurrenceInterval ?? 1),
            RecurrenceType.Yearly => CalculateNextYearDate(baseDate, task.RecurrenceInterval ?? 1),
            _ => null
        };
    }

    /// <summary>
    /// Рассчитать следующую дату при ежемесячном повторении, с сохранением дня выполнения и fallback на последний день месяца
    /// </summary>
    private DateTime CalculateNextMonthDate(DateTime baseDate, int monthsInterval)
    {
        if (monthsInterval <= 0) monthsInterval = 1;
        var target = baseDate.AddMonths(monthsInterval);
        var day = baseDate.Day;
        var daysInTarget = DateTime.DaysInMonth(target.Year, target.Month);
        var chosenDay = day > daysInTarget ? daysInTarget : day;
        return new DateTime(target.Year, target.Month, chosenDay);
    }

    /// <summary>
    /// Рассчитать следующую дату при ежегодном повторении, с fallback для 29 февраля на 28 февраля в невисокосный год
    /// </summary>
    private DateTime CalculateNextYearDate(DateTime baseDate, int yearsInterval)
    {
        if (yearsInterval <= 0) yearsInterval = 1;
        var targetYear = baseDate.Year + yearsInterval;
        var month = baseDate.Month;
        var day = baseDate.Day;

        var daysInTarget = DateTime.DaysInMonth(targetYear, month);
        var chosenDay = day > daysInTarget ? daysInTarget : day;
        return new DateTime(targetYear, month, chosenDay);
    }

    /// <summary>
    /// Рассчитать следующую дату для повторения по дням недели
    /// </summary>
    private DateTime CalculateNextWeekDayDate(DateTime baseDate, int weekDaysMask)
    {
        var currentDay = (int)baseDate.DayOfWeek;
        // Конвертируем DayOfWeek (0=Sunday) в нашу маску (1=Monday)
        var maskDay = currentDay == 0 ? 64 : 1 << (currentDay - 1);

        // Ищем следующий день недели из маски
        for (var i = 1; i <= 7; i++)
        {
            var nextDay = (currentDay + i) % 7;
            var nextMaskDay = nextDay == 0 ? 64 : 1 << (nextDay - 1);
            
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
        // Получаем задачу, чтобы рассчитать дату завершения так же, как в CompleteTask
        var task = GetById(taskId);
        if (task == null) return;

        // Для просроченных задач дата завершения должна быть равна дате назначения
        var completedDate = task.ActualAssignedDate ?? task.UserAssignedDate;
        if (completedDate != null && completedDate.Value.Date < DateTime.UtcNow.Date)
        {
            // Задача просрочена - используем дату назначения
            completedDate = completedDate.Value.Date;
        }
        else
        {
            // Задача не просрочена - используем текущую дату
            completedDate = DateTime.UtcNow;
        }

        // Обновляем статус и дату завершения
        UpdatePartial(taskId, task =>
        {
            task.Status = TaskStatus.Irrelevant;
            task.CompletedDate = completedDate;
        });

        // Для повторяющихся задач создаём следующий экземпляр (используя установленную completedDate)
        if (task.IsRecurring && task.RecurrenceType != RecurrenceType.None)
        {
            task.CompletedDate = completedDate;
            CreateNextRecurrence(task);
        }
    }

    public void RestoreFromIrrelevant(int taskId)
    {
        UpdatePartial(taskId, task =>
        {
            task.Status = TaskStatus.Planned;
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
                .Include(t => t.Relations)
                .Include(t => t.InverseRelations)
                .FirstOrDefault(e => e.Id == id);
                
            if (task == null) return;
            
            // Сохраняем ID тегов для проверки после удаления
            var tagIds = task.Tags.Select(tt => tt.TagId).ToList();

            // Удаляем связи, где эта задача является источником или целью
            var relations = Context.TaskRelations.Where(r => r.SourceTaskId == id || r.TargetTaskId == id).ToList();
            if (relations.Count != 0)
            {
                Context.TaskRelations.RemoveRange(relations);
            }
            
            // Удаляем задачу
            GetDbSet().Remove(task);
            Context.SaveChanges();
            
            // Уменьшаем usageCount для тегов, которые были связаны с удалённой задачей
            if (tagIds.Count != 0)
            {
                var tagRepo = new TagRepository(Context, NotificationService);
                foreach (var tagId in tagIds)
                {
                    try
                    {
                        tagRepo.DecrementUsage(tagId);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                try
                {
                    // Попробуем удалить физически неиспользуемые теги
                    var tagRepoCleanup = new TagRepository(Context, NotificationService);
                    tagRepoCleanup.CleanupUnusedTags(tagIds);
                }
                catch
                {
                    // ignore
                }
            }
            
            MarkDirty();
        }
    }

    public void DeleteRelation(int relationId)
    {
        lock (CacheLock)
        {
            var relation = Context.TaskRelations.Find(relationId);
            if (relation == null) return;
            Context.TaskRelations.Remove(relation);
            Context.SaveChanges();
            MarkDirty();
        }
    }

    public void ReopenTask(int taskId)
    {
        var task = GetById(taskId);
        if (task == null) return;

        UpdatePartial(taskId, t =>
        {
            t.Status = TaskStatus.Planned;
            t.CompletedDate = null;
        });
    }
}

/// <summary>
/// Репозиторий настроек
/// </summary>
public class SettingsRepository(StorageContext context, INotificationService notificationService) : CachedRepository<UserSettings>(context, notificationService), ISettingsRepository
{
    protected override DbSet<UserSettings> GetDbSet() => Context.Settings;

    public UserSettings GetUserSettings()
    {
        var settings = GetAll().FirstOrDefault();
        if (settings == null)
        {
            settings = new() { Id = 1 };
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
public class PriorityRepository(StorageContext context, INotificationService notificationService) : CachedRepository<PriorityLevel>(context, notificationService), IPriorityRepository
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
            for (var i = 0; i < orderedIds.Count; i++)
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
public class TagRepository(StorageContext context, INotificationService notificationService) : CachedRepository<Tag>(context, notificationService), ITagRepository
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
        if (tagIds == null || tagIds.Count == 0) return;
        
        lock (CacheLock)
        {
            foreach (var tag in tagIds
                         .Select(tagId => new { tagId, hasReferences = Context.TaskTags.Any(tt => tt.TagId == tagId) })
                         .Where(t => !t.hasReferences)
                         .Select(t => Context.Tags.Find(t.tagId)).OfType<Tag>())
            {
                Context.Tags.Remove(tag);
            }
            
            Context.SaveChanges();
            MarkDirty();
        }
    }

    public void DecrementUsage(int tagId)
    {
        // Атомарно уменьшаем UsageCount и удаляем тег, если он больше не используется
        if (tagId <= 0) return;

        lock (CacheLock)
        {
            var tag = Context.Tags.Find(tagId);
            if (tag == null) return;

            tag.UsageCount = Math.Max(0, tag.UsageCount - 1);

            // Если usageCount уменьшился до 0, и нет ссылок в TaskTags — удалим тег из базы
            var hasReferences = Context.TaskTags.Any(tt => tt.TagId == tagId);
            if (tag.UsageCount == 0 && !hasReferences)
            {
                Context.Tags.Remove(tag);
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
    public Tag? LastUsedTag { get; private set; }

    public void MarkTagUsed(Tag tag)
    {
        LastUsedTag = tag;
        tagRepository.IncrementUsage(tag.Id);
    }

    public List<Tag> GetSuggestedTags(int count = 5)
    {
        var result = new List<Tag>();

        // Сперва последний использованный тег из хипа (за сессию), если такой имеется и он ещё валиден
        if (LastUsedTag != null)
        {
            try
            {
                var existing = tagRepository.GetById(LastUsedTag.Id);
                if (existing != null && existing.UsageCount > 0)
                {
                    result.Add(existing);
                }
                else
                {
                    // Если тег удалён или больше не актуален — сбросим ссылку
                    LastUsedTag = null;
                }
            }
            catch
            {
                LastUsedTag = null;
            }
        }

        // Затем заполняем ещё 4 (или 5, если не было недавнего) по принципу самых используемых в созданных делах
        var remainingCount = count - result.Count;
        if (remainingCount <= 0) return result;
        var popular = tagRepository.GetPopularTags(remainingCount + 5); // Берем больше, чтобы исключить уже добавленные
        foreach (var tag in popular.TakeWhile(tag => result.Count < count).Where(tag => result.All(t => t.Id != tag.Id)))
        {
            result.Add(tag);
        }

        return result;
    }
}









