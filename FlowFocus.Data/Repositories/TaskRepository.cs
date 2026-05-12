using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Repositories;

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
            
            // Устанавливаем SourceTaskId/TargetTaskId для связей перед сохранением
            if (entity.Relations.Count != 0)
            {
                for (var i = 0; i < entity.Relations.Count; i++)
                {
                    var relation = entity.Relations[i];
                    // Если source не задан, назначаем текущую новую задачу как источник
                    if (relation.SourceTaskId == 0)
                    {
                        relation.SourceTaskId = entity.Id;
                    }

                    // Если target не задан, создаём новый объект TaskRelation с нужным TargetTaskId
                    // Так как TargetTaskId имеет init-only сеттер, нельзя присвоить его после создания объекта.
                    if (relation.TargetTaskId == 0)
                    {
                        var replacement = new TaskRelation
                        {
                            SourceTaskId = relation.SourceTaskId == 0 ? entity.Id : relation.SourceTaskId,
                            TargetTaskId = entity.Id,
                            Type = relation.Type,
                            LastChangesOn = DateTime.UtcNow
                        };

                        // Сохраняем Id для уже сохранённых записей (если было)
                        if (relation.Id > 0)
                        {
                            replacement.Id = relation.Id;
                        }

                        entity.Relations[i] = replacement;
                    }
                    else
                    {
                        relation.LastChangesOn = DateTime.UtcNow;
                    }
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
        // Удаляем только те существующие записи, которые не представлены в desired ни по Id, ни по комбинации Source/Target/Type.
        var toRemove = trackedAll.Where(r =>
            // Удаляем, если нет ни одного desired с тем же Id (если Id > 0)
            // И одновременно нет desired с тем же Source/Target/Type.
            !( (r.Id > 0 && desired.Any(d => d.Id > 0 && d.Id == r.Id))
               || desired.Any(d => d.SourceTaskId == r.SourceTaskId && d.TargetTaskId == r.TargetTaskId && d.Type == r.Type)
            )
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

            // Копируем теги и повышения приоритета основной задачи
            if (sourceTask.Tags != null && sourceTask.Tags.Count != 0)
            {
                newTask.Tags = sourceTask.Tags.Select(t => new TaskTag { TagId = t.TagId }).ToList();
            }

            if (sourceTask.PriorityEscalations != null && sourceTask.PriorityEscalations.Count != 0)
            {
                newTask.PriorityEscalations = sourceTask.PriorityEscalations
                    .Select(e => new PriorityEscalation { TargetPriorityId = e.TargetPriorityId, EscalationDate = e.EscalationDate, IsApplied = e.IsApplied })
                    .ToList();
            }

            // Копируем подзадачи рекурсивно (без сохранённых Id и без привязки к исходному RecurrenceSourceId)
            if (sourceTask.Subtasks != null && sourceTask.Subtasks.Count != 0)
            {
                newTask.Subtasks = sourceTask.Subtasks.Select(s => CloneTaskForRecurrence(s)).ToList();
            }

            Add(newTask);
        }
        catch (Exception ex)
        {
            // Логируем и не даём упасть приложению
            Console.WriteLine($"CreateNextRecurrence error for task {sourceTask?.Id}: {ex}");
        }
    }

    /// <summary>
    /// Рекурсивно клонирует задачу для использования в новой повторяющейся копии.
    /// Возвращает новый объект TaskItem с Id == 0 и без привязанных внешних ссылок (RecurrenceSourceId очищен).
    /// Копируются теги и правила повышения приоритета, а также вложенные подзадачи.
    /// </summary>
    private TaskItem CloneTaskForRecurrence(TaskItem source)
    {
        var clone = new TaskItem
        {
            // Id оставляем 0, EF/репозиторий назначит новый Id при добавлении
            Title = source.Title,
            Description = source.Description,
            Status = TaskStatus.Planned,
            PriorityId = source.PriorityId,
            Interest = source.Interest,
            Complexity = source.Complexity,
            EstimatedMinutes = source.EstimatedMinutes,
            IsFavorite = source.IsFavorite,
            HideUnderSpoiler = source.HideUnderSpoiler,
            // Подзадачи обычно не имеют собственной даты назначения — оставим null
            UserAssignedDate = null,
            ActualAssignedDate = null,
            IsRecurring = source.IsRecurring,
            RecurrenceType = source.RecurrenceType,
            RecurrenceInterval = source.RecurrenceInterval,
            RecurrenceWeekDays = source.RecurrenceWeekDays,
            // Не переносим RecurrenceSourceId — чтобы подзадачи не стали корневыми источниками повторения
            RecurrenceSourceId = null,
            CreatedDate = DateTime.UtcNow
        };

        // Копируем теги
        if (source.Tags != null && source.Tags.Count != 0)
        {
            clone.Tags = source.Tags.Select(t => new TaskTag { TagId = t.TagId }).ToList();
        }

        // Копируем повышения приоритета
        if (source.PriorityEscalations != null && source.PriorityEscalations.Count != 0)
        {
            clone.PriorityEscalations = source.PriorityEscalations
                .Select(e => new PriorityEscalation { TargetPriorityId = e.TargetPriorityId, EscalationDate = e.EscalationDate, IsApplied = e.IsApplied })
                .ToList();
        }

        // Рекурсивно копируем вложенные подзадачи
        if (source.Subtasks != null && source.Subtasks.Count != 0)
        {
            clone.Subtasks = source.Subtasks.Select(s => CloneTaskForRecurrence(s)).ToList();
        }

        return clone;
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