using FlowFocus.Core;
using FlowFocus.Core.Models;

namespace FlowFocus.Blazor.EditDialogContents.Validators;

public static class TaskEditValidator
{
    public record ValidationResult(bool IsValid, List<string> Errors);

    public static ValidationResult ValidateEscalations(IEnumerable<EscalationDto>? escalations, TaskItem task,
        List<PriorityLevel> priorities)
    {
        List<string> errors = [];
        var escalationList =
            escalations?.Where(e => e.EscalationDate != null).OrderBy(e => e.EscalationDate).ToList() ??
            [];

        var currentPriorityOrder = task.PriorityId.HasValue
            ? priorities.FirstOrDefault(p => p.Id == task.PriorityId)?.Order ?? 99
            : 99;

        var prevPriorityOrder = currentPriorityOrder;
        var prevDate = TodoDay.Today.ToDateTime();

        foreach (var escalation in escalationList)
        {
            var targetPriority = priorities.FirstOrDefault(p => p.Id == escalation.TargetPriorityId);
            if (targetPriority == null) continue;

            if (targetPriority.Order >= prevPriorityOrder)
            {
                errors.Add(
                    $"Нельзя задать повышение приоритета до \"{targetPriority.Name}\" — он не выше предыдущего.");
            }

            if (escalation.EscalationDate.HasValue && escalation.EscalationDate.Value.Date < TodoDay.Today.ToDateTime())
            {
                errors.Add($"Дата повышения {escalation.EscalationDate.Value:dd.MM.yyyy} уже в прошлом");
            }

            if (escalation.EscalationDate.HasValue && escalation.EscalationDate.Value.Date <= prevDate)
            {
                errors.Add("Дата повышения должна быть позже предыдущей даты.");
            }

            prevPriorityOrder = targetPriority.Order;
            prevDate = escalation.EscalationDate?.Date ?? prevDate;
        }

        return new(errors.Count == 0, errors);
    }

    public static ValidationResult ValidateRelations(IEnumerable<RelationDto>? relations, TaskItem task,
        List<PriorityLevel> priorities)
    {
        List<string> errors = [];
        var relationList = relations?.Where(r => r.TargetTask != null).ToList() ?? [];

        foreach (var relation in relationList)
        {
            if (relation.Type is not (Core.Enums.RelationType.Blocks or Core.Enums.RelationType.BlockedBy)) continue;
            var targetTask = relation.TargetTask;
            if (targetTask == null) continue;

            TaskItem blocker, blocked;
            if (relation.Type == Core.Enums.RelationType.Blocks)
            {
                blocker = task;
                blocked = targetTask;
            }
            else
            {
                blocker = targetTask;
                blocked = task;
            }

            var blockerDate = blocker.ScheduledDate;
            var blockedDate = blocked.ScheduledDate;

            if (blockerDate.HasValue && blockedDate.HasValue)
            {
                if (blockerDate.Value.Date > blockedDate.Value.Date)
                {
                    errors.Add(
                        $"Дата задачи \"{blocker.Title}\" ({blockerDate.Value:dd.MM.yyyy}) позже даты задачи, которую она блокирует ({blockedDate.Value:dd.MM.yyyy})");
                }
            }

            var blockerPriorityOrder = blocker.PriorityId.HasValue
                ? priorities.FirstOrDefault(p => p.Id == blocker.PriorityId)?.Order ?? 99
                : 99;
            var blockedPriorityOrder = blocked.PriorityId.HasValue
                ? priorities.FirstOrDefault(p => p.Id == blocked.PriorityId)?.Order ?? 99
                : 99;

            if (blockerPriorityOrder <= blockedPriorityOrder) continue;
            var blockerPriority = priorities.FirstOrDefault(p => p.Id == blocker.PriorityId)?.Name ?? "Не задан";
            var blockedPriority = priorities.FirstOrDefault(p => p.Id == blocked.PriorityId)?.Name ?? "Не задан";
            errors.Add(
                $"Приоритет задачи \"{blocker.Title}\" ({blockerPriority}) ниже приоритета связанной задачи ({blockedPriority})");
        }

        return new(errors.Count == 0, errors);
    }

    public static ValidationResult ValidateSubtasks(
        TaskItem task,
        IEnumerable<SubtaskDto>? subtasks,
        List<PriorityLevel> priorities,
        ITaskRepository taskRepo)
    {
        List<string> errors = [];

        var parentTask = task.ParentTask;
        if (parentTask == null && task.ParentTaskId is > 0)
        {
            parentTask = taskRepo.GetById(task.ParentTaskId.Value);
        }

        if (parentTask != null)
        {
            try
            {
                var taskPriority = task.Priority ?? (task.PriorityId.HasValue ? priorities.FirstOrDefault(p => p.Id == task.PriorityId) : null);
                var parentPriority = parentTask.Priority ?? (parentTask.PriorityId.HasValue ? priorities.FirstOrDefault(p => p.Id == parentTask.PriorityId) : null);

                var taskWrapper = new TaskItem
                {
                    Id = task.Id,
                    Title = task.Title,
                    PriorityId = task.PriorityId,
                    Priority = taskPriority,
                    ScheduledDate = task.ScheduledDate,
                    ParentTaskId = task.ParentTaskId
                };

                var parentWrapper = new TaskItem
                {
                    Id = parentTask.Id,
                    Title = parentTask.Title,
                    PriorityId = parentTask.PriorityId,
                    Priority = parentPriority,
                    ScheduledDate = parentTask.ScheduledDate,
                    ParentTask = parentTask.ParentTask
                };

                Core.Validation.TaskHierarchyValidator.ValidateSubtaskParent(parentWrapper, taskWrapper);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (task.Id <= 0) return new(errors.Count == 0, errors);
        var existingTracked = taskRepo.GetById(task.Id);
        if (existingTracked?.Subtasks != null)
        {
            var taskPriority = task.Priority ?? (task.PriorityId.HasValue ? priorities.FirstOrDefault(p => p.Id == task.PriorityId) : null);
            var parentWrapper = new TaskItem
            {
                Id = task.Id,
                Title = task.Title,
                PriorityId = task.PriorityId,
                Priority = taskPriority,
                ScheduledDate = task.ScheduledDate
            };

            foreach (var sub in existingTracked.Subtasks)
            {
                if (subtasks is not null && subtasks.Any(s => s.Id == sub.Id && s.IsDeleted))
                    continue;

                var subPriority = sub.Priority ?? (sub.PriorityId.HasValue ? priorities.FirstOrDefault(p => p.Id == sub.PriorityId) : null);
                var subWrapper = new TaskItem
                {
                    Id = sub.Id,
                    Title = sub.Title,
                    PriorityId = sub.PriorityId,
                    Priority = subPriority,
                    ScheduledDate = sub.ScheduledDate,
                    ParentTaskId = sub.ParentTaskId
                };

                try
                {
                    Core.Validation.TaskHierarchyValidator.ValidateSubtaskParent(parentWrapper, subWrapper);
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add($"Подзадача \"{sub.Title}\": {ex.Message}");
                }
            }
        }

        return new(errors.Count == 0, errors);
    }
}