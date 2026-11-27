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
            var targetTask = relation.TargetTask;
            if (targetTask == null) continue;

            if (ReferenceEquals(targetTask, task) || (targetTask.Id > 0 && task.Id > 0 && targetTask.Id == task.Id))
            {
                errors.Add($"Задача \"{task.Title}\" не может создавать связь сама с собой.");
                continue;
            }

            if (relation.Type is not (Core.Enums.RelationType.Blocks or Core.Enums.RelationType.BlockedBy)) continue;

            if (task.Status is Core.Enums.TaskStatus.Completed or Core.Enums.TaskStatus.Irrelevant) continue;
            if (targetTask.IsInactive) continue;

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

            // При редактировании самой заблокированной задачи (BlockedBy) разрешаем повышение приоритета:
            // после сохранения планировщик автоматически поднимет приоритеты активных блокеров.
            // При редактировании блокера (Blocks) сохраняем валидацию приоритета.
            if (relation.Type != Core.Enums.RelationType.Blocks) continue;

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
