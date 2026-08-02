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
public class TaskRepository(StorageContext context, INotificationService notificationService) 
    : CachedRepository<TaskItem>(context, notificationService), ITaskRepository
{
    private readonly Lazy<TagRepository> _tagRepository = new(() => new TagRepository(context, notificationService));

    protected override DbSet<TaskItem> GetDbSet() => Context.Tasks;

    #region CRUD Overrides

    public override void Add(TaskItem entity)
    {
        lock (CacheLock)
        {
            if (entity.Id == 0)
            {
                entity.Id = GetNextId();
            }

            entity.LastChangesOn = DateTime.UtcNow;

            PrepareSubtasksForAdd(entity);
            PrepareRelationsForAdd(entity);
            PrepareEscalationsForAdd(entity);

            GetDbSet().Add(entity);
            Context.SaveChanges();
            MarkDirty();
        }
    }

    public override void Update(TaskItem entity)
    {
        lock (CacheLock)
        {
            if (entity.Id <= 0)
            {
                throw new InvalidOperationException($"Invalid entity ID: {entity.Id}. Entity must have a valid ID > 0 for update.");
            }

            var trackedEntity = GetTrackedQuery()
                .Include(t => t.Tags)
                .Include(t => t.Subtasks)
                .Include(t => t.Relations)
                .Include(t => t.InverseRelations)
                .Include(t => t.PriorityEscalations)
                .FirstOrDefault(e => e.Id == entity.Id)
                ?? throw new InvalidOperationException($"Entity with ID {entity.Id} not found");

            Context.Entry(trackedEntity).CurrentValues.SetValues(entity);

            UpdateTags(trackedEntity, entity);
            UpdateSubtasks(trackedEntity, entity);
            UpdateRelations(trackedEntity, entity);
            UpdateEscalations(trackedEntity, entity);

            trackedEntity.LastChangesOn = DateTime.UtcNow;
            Context.SaveChanges();
            MarkDirty();
        }
    }

    public override void Delete(int id)
    {
        lock (CacheLock)
        {
            var task = GetTrackedQuery()
                .Include(t => t.Tags)
                .FirstOrDefault(e => e.Id == id);

            if (task == null) return;

            var tagIds = task.Tags.Select(tt => tt.TagId).ToList();

            var relations = Context.TaskRelations.Where(r => r.SourceTaskId == id || r.TargetTaskId == id).ToList();
            if (relations.Count != 0)
            {
                Context.TaskRelations.RemoveRange(relations);
            }

            GetDbSet().Remove(task);
            Context.SaveChanges();

            CleanupTagsIfAny(tagIds);
            MarkDirty();
        }
    }

    #endregion

    #region Add Helpers

    private static void PrepareSubtasksForAdd(TaskItem entity)
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

    private static void PrepareRelationsForAdd(TaskItem entity)
    {
        for (var i = 0; i < entity.Relations.Count; i++)
        {
            var relation = entity.Relations[i];
            
            if (relation.SourceTaskId == 0)
            {
                relation.SourceTaskId = entity.Id;
            }

            if (relation.TargetTaskId == 0)
            {
                entity.Relations[i] = new TaskRelation
                {
                    Id = relation.Id > 0 ? relation.Id : 0,
                    SourceTaskId = relation.SourceTaskId == 0 ? entity.Id : relation.SourceTaskId,
                    TargetTaskId = entity.Id,
                    Type = relation.Type,
                    LastChangesOn = DateTime.UtcNow
                };
            }
            else
            {
                relation.LastChangesOn = DateTime.UtcNow;
            }
        }
    }

    private static void PrepareEscalationsForAdd(TaskItem entity)
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

    #endregion

    #region Update Helpers

    private void UpdateTags(TaskItem tracked, TaskItem source)
    {
        var sourceTagIds = source.Tags.Select(st => st.TagId).ToHashSet();
        var trackedTagIds = tracked.Tags.Select(tt => tt.TagId).ToHashSet();

        var tagsToRemove = tracked.Tags.Where(tt => !sourceTagIds.Contains(tt.TagId)).ToList();
        var removedTagIds = tagsToRemove.Select(tt => tt.TagId).ToList();

        foreach (var tag in tagsToRemove)
        {
            Context.TaskTags.Remove(tag);
        }

        foreach (var sourceTag in source.Tags.Where(st => !trackedTagIds.Contains(st.TagId)))
        {
            Context.TaskTags.Add(new TaskTag
            {
                TaskId = tracked.Id,
                TagId = sourceTag.TagId
            });
        }

        CleanupTagsIfAny(removedTagIds);
    }

    private void UpdateSubtasks(TaskItem tracked, TaskItem source)
    {
        var subtasksToRemove = tracked.Subtasks
            .Where(st => !source.Subtasks.Any(sst => sst.Id == st.Id && sst.Id > 0))
            .ToList();

        foreach (var subtask in subtasksToRemove)
        {
            Context.Tasks.Remove(subtask);
        }

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
        var desired = source.Relations ?? [];
        var trackedAll = (tracked.Relations ?? []).Concat(tracked.InverseRelations ?? []).ToList();

        var toRemove = trackedAll.Where(r =>
            !((r.Id > 0 && desired.Any(d => d.Id > 0 && d.Id == r.Id)) ||
              desired.Any(d => d.SourceTaskId == r.SourceTaskId && d.TargetTaskId == r.TargetTaskId && d.Type == r.Type))
        ).ToList();

        foreach (var rel in toRemove)
        {
            Context.TaskRelations.Remove(rel);
        }

        foreach (var desiredRel in desired)
        {
            TaskRelation? existing = null;

            if (desiredRel.Id > 0)
            {
                existing = trackedAll.FirstOrDefault(r => r.Id == desiredRel.Id);
            }

            existing ??= trackedAll.FirstOrDefault(r => 
                r.SourceTaskId == desiredRel.SourceTaskId && 
                r.TargetTaskId == desiredRel.TargetTaskId && 
                r.Type == desiredRel.Type);

            if (existing != null)
            {
                Context.Entry(existing).CurrentValues.SetValues(desiredRel);
            }
            else
            {
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
        var escalationsToRemove = tracked.PriorityEscalations
            .Where(e => !source.PriorityEscalations.Any(se => se.Id == e.Id && se.Id > 0))
            .ToList();

        foreach (var escalation in escalationsToRemove)
        {
            Context.PriorityEscalations.Remove(escalation);
        }

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

    private void CleanupTagsIfAny(List<int> tagIds)
    {
        if (tagIds.Count == 0) return;

        var tagRepo = _tagRepository.Value;
        foreach (var id in tagIds)
        {
            try { tagRepo.DecrementUsage(id); } catch { /* Не фатально */ }
        }

        try { tagRepo.CleanupUnusedTags(tagIds); } catch { /* Не фатально */ }
    }

    #endregion

    #region Queries & Filters

    protected override IQueryable<TaskItem> GetBaseQuery() =>
        GetDbSet()
            .AsNoTracking()
            .Include(t => t.Priority)
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.Relations).ThenInclude(r => r.TargetTask)
            .Include(t => t.InverseRelations).ThenInclude(r => r.SourceTask)
            .Include(t => t.Subtasks)
            .Include(t => t.PriorityEscalations).ThenInclude(pe => pe.TargetPriority);

    private IEnumerable<TaskItem> GetActiveRootTasks() =>
        GetAll().Where(t => t.ParentTaskId == null && t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant);

    public List<TaskItem> GetTasksForDate(DateTime date) =>
        GetActiveRootTasks()
            .Where(t => t.ScheduledDate != null && t.ScheduledDate.Value.Date == date.Date)
            .ToList();

    public List<TaskItem> GetTodayTasks()
    {
        var today = TodoDay.Today;
        return GetActiveRootTasks()
            .Where(t => t.ScheduledDate != null && today.IsSameDay(t.ScheduledDate))
            .ToList();
    }

    public List<TaskItem> GetTomorrowTasks() => GetTasksForDate(TodoDay.Today.Tomorrow.ToDateTime());

    public List<TaskItem> GetNotConfiguredTasks() =>
        GetAll().Where(t => t.ParentTaskId == null && t.Status == TaskStatus.NotConfigured).ToList();

    public int GetNotConfiguredCount() =>
        GetAll().Count(t => t.ParentTaskId == null && t.Status == TaskStatus.NotConfigured);

    public List<TaskItem> GetOverdueTasks()
    {
        var today = TodoDay.Today;
        return GetActiveRootTasks()
            .Where(t => t.ScheduledDate != null && today.IsOverdue(t.ScheduledDate))
            .ToList();
    }

    public List<TaskItem> GetTasksUnblockedBy(int taskId) =>
        GetAll()
            .Where(t => t.InverseRelations.Any(r => r.Type == RelationType.Blocks && r.SourceTaskId == taskId))
            .Where(t => !t.InverseRelations
                .Where(r => r.Type == RelationType.Blocks && r.SourceTaskId != taskId)
                .Select(r => r.SourceTask)
                .Any(blocker => blocker != null && blocker.Status != TaskStatus.Completed && blocker.Status != TaskStatus.Irrelevant))
            .ToList();

    public List<TaskItem> GetTasksForAutocomplete() =>
        GetActiveRootTasks()
            .OrderBy(t => t.Title)
            .ToList();

    public TaskItem? GetProcrastinationTask(List<int> excludeIds) =>
        GetAll()
            .Where(t => t.ParentTaskId == null &&
                        t is { Interest: >= AppConfig.MinProcrastinationInterest, Status: TaskStatus.Planned } &&
                        !excludeIds.Contains(t.Id))
            .OrderByDescending(t => t.Interest - Math.Sqrt(t.Priority?.Order ?? 99))
            .FirstOrDefault();

    public TaskItem? GetLeastPriorityTaskOfDay()
    {
        var today = TodoDay.Today;
        return GetActiveRootTasks()
            .Where(t => t.ScheduledDate != null && today.IsSameDay(t.ScheduledDate) && t.Status == TaskStatus.Planned)
            .OrderByDescending(t => t.Priority?.Order ?? 99)
            .ThenBy(t => t.Interest ?? 0)
            .FirstOrDefault();
    }

    public List<TaskItem> GetRecurringCandidatesForPlanner()
    {
        var today = TodoDay.Today;

        return Context.Tasks
            .AsNoTracking()
            .Where(t => t.ParentTaskId == null &&
                        t.Status != TaskStatus.Completed &&
                        t.Status != TaskStatus.Irrelevant &&
                        t.Status != TaskStatus.NotConfigured &&
                        t.IsRecurring)
            .ToList()
            .Where(t => (t.DateSource != DateSource.Manual || today.IsOverdue(t.ScheduledDate)) &&
                        (today.IsOverdue(t.ScheduledDate) || t.ScheduledDate == null))
            .ToList();
    }

    #endregion

    #region Task Status & Lifecycle Management

    public void CompleteTask(int taskId) => SetTaskStatusAndHandleRecurrence(taskId, TaskStatus.Completed);

    public void MarkIrrelevant(int taskId) => SetTaskStatusAndHandleRecurrence(taskId, TaskStatus.Irrelevant);

    private void SetTaskStatusAndHandleRecurrence(int taskId, TaskStatus targetStatus)
    {
        var task = GetById(taskId);
        if (task == null) return;

        var completedDate = DetermineCompletionDate(task.ScheduledDate);

        UpdatePartial(taskId, t =>
        {
            t.Status = targetStatus;
            t.CompletedDate = completedDate;
        });

        if (task.IsRecurring && task.RecurrenceType != RecurrenceType.None)
        {
            task.CompletedDate = completedDate;
            CreateNextRecurrence(task);
        }
    }

    public void RestoreFromIrrelevant(int taskId) => UpdateTaskStatus(taskId, TaskStatus.Planned);

    public void ReopenTask(int taskId) =>
        UpdatePartial(taskId, t =>
        {
            t.Status = TaskStatus.Planned;
            t.CompletedDate = null;
        });

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

    public void ApplyPriorityEscalation(int taskId, int targetPriorityId, IEnumerable<int> appliedEscalationIds)
    {
        lock (CacheLock)
        {
            var trackedTask = Context.Tasks
                .Include(t => t.PriorityEscalations)
                .FirstOrDefault(t => t.Id == taskId);

            if (trackedTask == null) return;

            trackedTask.PriorityId = targetPriorityId;
            trackedTask.LastChangesOn = DateTime.UtcNow;

            var escalationSet = appliedEscalationIds.ToHashSet();
            foreach (var escalation in trackedTask.PriorityEscalations.Where(e => escalationSet.Contains(e.Id)))
            {
                escalation.IsApplied = true;
                escalation.LastChangesOn = DateTime.UtcNow;
            }

            Context.SaveChanges();
            MarkDirty();
        }
    }

    public void NormalizeTaskDateSources()
    {
        lock (CacheLock)
        {
            var tasksToNormalize = Context.Tasks
                .Where(t => t.ScheduledDate == null && (t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed))
                .ToList();

            if (tasksToNormalize.Count == 0) return;

            foreach (var task in tasksToNormalize)
            {
                task.DateSource = DateSource.AutoFlexible;
                task.LastChangesOn = DateTime.UtcNow;
            }

            Context.SaveChanges();
            MarkDirty();
        }
    }

    public void UpdateTaskSchedule(int taskId, DateTime? scheduledDate, DateSource? dateSource = null) =>
        UpdatePartial(taskId, t =>
        {
            t.ScheduledDate = scheduledDate;
            if (dateSource.HasValue)
            {
                t.DateSource = dateSource.Value;
            }
        });

    public void UpdateTaskStatus(int taskId, TaskStatus status) =>
        UpdatePartial(taskId, t => t.Status = status);

    public void MutateRecurringTaskInPlace(int taskId, DateTime assignedDate) =>
        UpdatePartial(taskId, t =>
        {
            t.ScheduledDate = assignedDate;
            t.DateSource = DateSource.AutoFixed;
        });

    private static DateTime DetermineCompletionDate(DateTime? scheduledDate)
    {
        var today = TodoDay.Today;
        return (scheduledDate != null && today.IsOverdue(scheduledDate))
            ? scheduledDate.Value.Date
            : today.ToDateTime();
    }

    #endregion

    #region Recurrence Rules

    private void CreateNextRecurrence(TaskItem sourceTask)
    {
        try
        {
            var nextDate = CalculateNextRecurrenceDate(sourceTask);
            if (nextDate == null) return;

            var sourceId = sourceTask.RecurrenceSourceId ?? sourceTask.Id;
            var start = nextDate.Value.Date;
            var end = start.AddDays(1);

            var exists = Context.Tasks
                .AsNoTracking()
                .Any(t => ((t.RecurrenceSourceId.HasValue && t.RecurrenceSourceId.Value == sourceId) ||
                           (!t.RecurrenceSourceId.HasValue && t.Id == sourceId))
                          && t.ScheduledDate.HasValue && t.ScheduledDate.Value >= start && t.ScheduledDate.Value < end);

            if (exists) return;

            var newTask = CloneTaskItem(sourceTask, isParent: true, scheduledDate: nextDate, dateSource: DateSource.AutoFixed, recurrenceSourceId: sourceId);

            Add(newTask);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CreateNextRecurrence error for task {sourceTask?.Id}: {ex}");
        }
    }

    private TaskItem CloneTaskItem(
        TaskItem source, 
        bool isParent, 
        DateTime? scheduledDate = null, 
        DateSource? dateSource = null, 
        int? recurrenceSourceId = null) => new()
    {
        Title = source.Title,
        Description = source.Description,
        Status = TaskStatus.Planned,
        PriorityId = source.PriorityId,
        Interest = source.Interest,
        Complexity = source.Complexity,
        EstimatedMinutes = source.EstimatedMinutes,
        IsFavorite = source.IsFavorite,
        HideUnderSpoiler = source.HideUnderSpoiler,
        ScheduledDate = isParent ? (scheduledDate ?? source.ScheduledDate) : null,
        DateSource = isParent ? (dateSource ?? source.DateSource) : DateSource.AutoFlexible,
        IsRecurring = source.IsRecurring,
        RecurrenceType = source.RecurrenceType,
        RecurrenceInterval = source.RecurrenceInterval,
        RecurrenceWeekDays = source.RecurrenceWeekDays,
        RecurrenceSourceId = isParent ? (recurrenceSourceId ?? source.RecurrenceSourceId) : null,
        CreatedDate = DateTime.UtcNow,
        Tags = source.Tags?.Select(t => new TaskTag { TagId = t.TagId }).ToList() ?? [],
        PriorityEscalations = source.PriorityEscalations?
            .Select(e => new PriorityEscalation { TargetPriorityId = e.TargetPriorityId, EscalationDate = e.EscalationDate, IsApplied = e.IsApplied })
            .ToList() ?? [],
        Subtasks = source.Subtasks?.Select(s => CloneTaskItem(s, isParent: false)).ToList() ?? []
    };

    private static DateTime? CalculateNextRecurrenceDate(TaskItem task)
    {
        var completedDate = task.CompletedDate ?? TodoDay.Today.ToDateTime();
        var assignedDate = task.ScheduledDate ?? completedDate;
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

    private static DateTime CalculateNextMonthDate(DateTime baseDate, int monthsInterval)
    {
        var interval = monthsInterval <= 0 ? 1 : monthsInterval;
        var target = baseDate.AddMonths(interval);
        var daysInTarget = DateTime.DaysInMonth(target.Year, target.Month);
        return new DateTime(target.Year, target.Month, Math.Min(baseDate.Day, daysInTarget));
    }

    private static DateTime CalculateNextYearDate(DateTime baseDate, int yearsInterval)
    {
        var interval = yearsInterval <= 0 ? 1 : yearsInterval;
        var targetYear = baseDate.Year + interval;
        var daysInTarget = DateTime.DaysInMonth(targetYear, baseDate.Month);
        return new DateTime(targetYear, baseDate.Month, Math.Min(baseDate.Day, daysInTarget));
    }

    private static DateTime CalculateNextWeekDayDate(DateTime baseDate, int weekDaysMask)
    {
        var currentDay = (int)baseDate.DayOfWeek;

        for (var i = 1; i <= 7; i++)
        {
            var nextDay = (currentDay + i) % 7;
            var nextMaskDay = nextDay == 0 ? 64 : 1 << (nextDay - 1);

            if ((weekDaysMask & nextMaskDay) != 0)
            {
                return baseDate.AddDays(i);
            }
        }

        return baseDate.AddDays(7);
    }

    #endregion
}