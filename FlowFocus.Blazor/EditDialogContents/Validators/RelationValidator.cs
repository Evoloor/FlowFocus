using FlowFocus.Core.Models;
using static FlowFocus.Blazor.EditDialogContents.Validators.TaskEditValidator;

namespace FlowFocus.Blazor.EditDialogContents.Validators;

public static class RelationValidator
{
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
}
