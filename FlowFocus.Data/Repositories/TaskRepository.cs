using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data.Repositories.Helpers;
using FlowFocus.Data.Services;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Repositories;

/// <summary>
/// Репозиторий задач (Чистый Data Access & Queries)
/// </summary>
public class TaskRepository : CachedRepository<TaskItem>, ITaskRepository
{
    private readonly Lazy<TagRepository> _tagRepository;
    private readonly ITaskRecurrenceService _recurrenceService;

    public TaskRepository(
        StorageContext context,
        INotificationService notificationService,
        ITaskRecurrenceService? recurrenceService = null)
        : base(context, notificationService)
    {
        _tagRepository = new(() => new(context, notificationService));
        _recurrenceService = recurrenceService ?? new TaskRecurrenceService();
        notificationService.OnTasksChanged += RefreshCache;
    }

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

            TaskGraphSyncHelper.PrepareSubtasksForAdd(Context, entity);
            TaskGraphSyncHelper.PrepareRelationsForAdd(entity);
            TaskGraphSyncHelper.PrepareEscalationsForAdd(entity);

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
                                    .Include(t => t.Conditions).ThenInclude(tc => tc.Condition)
                                    .Include(t => t.Subtasks)
                                    .Include(t => t.Relations)
                                    .Include(t => t.InverseRelations)
                                    .Include(t => t.PriorityEscalations)
                                    .FirstOrDefault(e => e.Id == entity.Id)
                                ?? throw new InvalidOperationException($"Entity with ID {entity.Id} not found");

            if (trackedEntity.ParentTaskId != null)
            {
                var parent = GetById(trackedEntity.ParentTaskId.Value);
                if (parent != null)
                {
                    TaskHierarchyValidator.ValidateSubtaskParent(parent, entity);
                }
            }

            Context.Entry(trackedEntity).CurrentValues.SetValues(entity);

            var removedTagIds = TaskGraphSyncHelper.UpdateTags(Context, trackedEntity, entity);
            TaskGraphSyncHelper.UpdateConditions(Context, trackedEntity, entity);
            TaskGraphSyncHelper.UpdateSubtasks(Context, trackedEntity, entity);
            TaskGraphSyncHelper.UpdateRelations(Context, trackedEntity, entity);
            TaskGraphSyncHelper.UpdateEscalations(Context, trackedEntity, entity);

            if (trackedEntity.Status != TaskStatus.Completed && trackedEntity.Status != TaskStatus.Irrelevant)
            {
                if (FlowFocus.Core.Helpers.TaskStatusCalculator.IsTaskBlocked(trackedEntity))
                {
                    trackedEntity.Status = TaskStatus.Blocked;
                }
            }

            trackedEntity.LastChangesOn = DateTime.UtcNow;
            Context.SaveChanges();

            CleanupTagsIfAny(removedTagIds);
            MarkDirty();
        }
    }

    public override void Delete(int id)
    {
        lock (CacheLock)
        {
            var task = GetTrackedQuery()
                .Include(t => t.Tags)
                .Include(t => t.Conditions)
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
            .Include(t => t.Conditions).ThenInclude(tc => tc.Condition)
            .Include(t => t.Relations).ThenInclude(r => r.TargetTask)
            .Include(t => t.InverseRelations).ThenInclude(r => r.SourceTask)
            .Include(t => t.Subtasks)
            .Include(t => t.PriorityEscalations).ThenInclude(pe => pe.TargetPriority);

    private IEnumerable<TaskItem> GetActiveRootTasks() => GetAll().FilterActiveRootTasks();

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
            .Where(t => !t.Conditions.Any(c => c.Condition != null && !c.Condition.IsActive))
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

    public TaskItem? GetProcrastinationTask(List<int> excludeIds) => GetAll().FindProcrastinationTask(excludeIds);

    public TaskItem? GetLeastPriorityTaskOfDay() => GetAll().FindLeastPriorityTaskOfDay();

    public List<TaskItem> GetRecurringCandidatesForPlanner() => Context.FindRecurringCandidatesForPlanner();

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

        if (targetStatus is TaskStatus.Completed or TaskStatus.Irrelevant)
        {
            var incomingBlockers = Context.TaskRelations
                .Where(r => r.TargetTaskId == taskId && r.Type == RelationType.Blocks)
                .ToList();
            if (incomingBlockers.Count > 0)
            {
                Context.TaskRelations.RemoveRange(incomingBlockers);
                Context.SaveChanges();
            }
        }

        if (task.IsRecurring && task.RecurrenceType != RecurrenceType.None)
        {
            task.CompletedDate = completedDate;
            _recurrenceService.HandleTaskCompletionRecurrence(
                task,
                (sourceId, start, end) => Context.Tasks.AsNoTracking().Any(t =>
                    ((t.RecurrenceSourceId.HasValue && t.RecurrenceSourceId.Value == sourceId) ||
                     (!t.RecurrenceSourceId.HasValue && t.Id == sourceId))
                    && t.ScheduledDate.HasValue && t.ScheduledDate.Value >= start && t.ScheduledDate.Value < end),
                Add);
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

    public void ApplyPriorityEscalation(int taskId, int targetPriorityId, IEnumerable<int> appliedEscalationIds, bool saveChanges = true)
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

            if (saveChanges)
            {
                Context.SaveChanges();
                MarkDirty();
            }
        }
    }

    public void NormalizeTaskDateSources(bool saveChanges = true)
    {
        lock (CacheLock)
        {
            var hasChanges = TaskDateNormalizer.NormalizeDateSources(Context, _recurrenceService);
            if (hasChanges && saveChanges)
            {
                Context.SaveChanges();
                MarkDirty();
            }
        }
    }

    public void NormalizeTaskRelations(bool saveChanges = true)
    {
        lock (CacheLock)
        {
            var hasChanges = RelationNormalizer.NormalizeTaskRelations(Context, _tagRepository.Value);
            if (hasChanges && saveChanges)
            {
                Context.SaveChanges();
                MarkDirty();
            }
        }
    }

    public void UpdateTaskSchedule(int taskId, DateTime? scheduledDate, DateSource? dateSource = null, bool saveChanges = true) =>
        UpdatePartial(taskId, t =>
        {
            t.ScheduledDate = scheduledDate;
            if (dateSource.HasValue)
            {
                t.DateSource = dateSource.Value;
            }
        }, saveChanges);

    public void UpdateTaskStatus(int taskId, TaskStatus status, bool saveChanges = true) =>
        UpdatePartial(taskId, t => t.Status = status, saveChanges);

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
}