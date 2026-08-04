using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests.Builders;

public class TaskItemBuilder
{
    private int _id = 1;
    private string _title = "Test Task";
    private string? _description;
    private TaskStatus _status = TaskStatus.Planned;
    private bool _isFavorite;
    private int? _priorityId;
    private PriorityLevel? _priority;
    private int? _interest = 5;
    private int? _complexity = 10;
    private int? _estimatedMinutes = 30;
    private DateTime? _scheduledDate;
    private DateSource _dateSource = DateSource.AutoFlexible;
    private DateTime? _completedDate;
    private readonly DateTime _createdDate = DateTime.UtcNow;
    private bool _isRecurring;
    private RecurrenceType _recurrenceType = RecurrenceType.None;
    private int? _recurrenceInterval;
    private int? _recurrenceWeekDays;
    private int? _recurrenceSourceId;
    private int? _parentTaskId;
    private TaskItem? _parentTask;

    public TaskItemBuilder WithDescription(string? description)
    {
        _description = description;
        return this;
    }

    public TaskItemBuilder WithFavorite(bool isFavorite = true)
    {
        _isFavorite = isFavorite;
        return this;
    }

    public TaskItemBuilder WithRecurrenceSourceId(int? sourceId)
    {
        _recurrenceSourceId = sourceId;
        return this;
    }

    public TaskItemBuilder WithParentTask(TaskItem? parentTask)
    {
        _parentTask = parentTask;
        _parentTaskId = parentTask?.Id;
        return this;
    }
    private readonly List<TaskItem> _subtasks = [];
    private readonly List<TaskRelation> _relations = [];
    private readonly List<TaskRelation> _inverseRelations = [];
    private readonly List<PriorityEscalation> _priorityEscalations = [];

    public TaskItemBuilder WithId(int id)
    {
        _id = id;
        return this;
    }

    public TaskItemBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public TaskItemBuilder WithStatus(TaskStatus status)
    {
        _status = status;
        return this;
    }

    public TaskItemBuilder WithPriority(PriorityLevel priority)
    {
        _priority = priority;
        _priorityId = priority.Id;
        return this;
    }

    public TaskItemBuilder WithPriorityId(int? priorityId)
    {
        _priorityId = priorityId;
        return this;
    }

    public TaskItemBuilder WithInterest(int? interest)
    {
        _interest = interest;
        return this;
    }

    public TaskItemBuilder WithComplexity(int? complexity)
    {
        _complexity = complexity;
        return this;
    }

    public TaskItemBuilder WithEstimatedMinutes(int? minutes)
    {
        _estimatedMinutes = minutes;
        return this;
    }

    public TaskItemBuilder WithScheduledDate(DateTime? date, DateSource dateSource = DateSource.Manual)
    {
        _scheduledDate = date;
        _dateSource = dateSource;
        return this;
    }

    public TaskItemBuilder WithDateSource(DateSource source)
    {
        _dateSource = source;
        return this;
    }

    public TaskItemBuilder WithCompletedDate(DateTime? date)
    {
        _completedDate = date;
        if (date.HasValue)
        {
            _status = TaskStatus.Completed;
        }
        return this;
    }

    public TaskItemBuilder WithRecurrence(RecurrenceType type, int? interval = null, int? weekDays = null)
    {
        _isRecurring = type != RecurrenceType.None;
        _recurrenceType = type;
        _recurrenceInterval = interval;
        _recurrenceWeekDays = weekDays;
        return this;
    }

    public TaskItemBuilder WithSubtask(TaskItem subtask)
    {
        _subtasks.Add(subtask);
        return this;
    }

    public TaskItemBuilder WithParentTaskId(int? parentId)
    {
        _parentTaskId = parentId;
        return this;
    }

    public TaskItemBuilder WithRelation(TaskItem target, RelationType type = RelationType.Blocks)
    {
        _relations.Add(new()
        {
            SourceTaskId = _id,
            TargetTaskId = target.Id,
            TargetTask = target,
            Type = type
        });
        return this;
    }

    public TaskItemBuilder WithInverseRelation(TaskItem source, RelationType type = RelationType.Blocks)
    {
        _inverseRelations.Add(new()
        {
            SourceTaskId = source.Id,
            SourceTask = source,
            TargetTaskId = _id,
            Type = type
        });
        return this;
    }

    public TaskItemBuilder WithEscalation(int targetPriorityId, DateTime escalationDate)
    {
        _priorityEscalations.Add(new()
        {
            TaskId = _id,
            TargetPriorityId = targetPriorityId,
            EscalationDate = escalationDate,
            IsApplied = false
        });
        return this;
    }

    public TaskItem Build()
    {
        TaskItem item = new()
        {
            Id = _id,
            Title = _title,
            Description = _description,
            Status = _status,
            IsFavorite = _isFavorite,
            PriorityId = _priorityId,
            Priority = _priority,
            Interest = _interest,
            Complexity = _complexity,
            EstimatedMinutes = _estimatedMinutes,
            ScheduledDate = _scheduledDate,
            DateSource = _dateSource,
            CompletedDate = _completedDate,
            CreatedDate = _createdDate,
            IsRecurring = _isRecurring,
            RecurrenceType = _recurrenceType,
            RecurrenceInterval = _recurrenceInterval,
            RecurrenceWeekDays = _recurrenceWeekDays,
            RecurrenceSourceId = _recurrenceSourceId,
            ParentTaskId = _parentTaskId,
            ParentTask = _parentTask,
            Subtasks = [.. _subtasks],
            Relations = [.. _relations],
            InverseRelations = [.. _inverseRelations],
            PriorityEscalations = [.. _priorityEscalations],
            LastChangesOn = DateTime.UtcNow
        };

        foreach (var subtask in item.Subtasks)
        {
            subtask.ParentTaskId = item.Id;
        }

        return item;
    }
}
