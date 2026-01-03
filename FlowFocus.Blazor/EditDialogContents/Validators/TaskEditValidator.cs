using FlowFocus.Core.Models;

namespace FlowFocus.Blazor.EditDialogContents.Validators;

public static class TaskEditValidator
{
    public record ValidationResult(bool IsValid, List<string> Errors);

    public static ValidationResult ValidateEscalations(IEnumerable<EscalationDto>? escalations, TaskItem task, List<PriorityLevel> priorities)
    {
        var errors = new List<string>();
        var escalationList = escalations?.Where(e => e.EscalationDate != null).OrderBy(e => e.EscalationDate).ToList() ??
                             [];

        var currentPriorityOrder = task.PriorityId.HasValue
            ? priorities.FirstOrDefault(p => p.Id == task.PriorityId)?.Order ?? 99
            : 99;

        var prevPriorityOrder = currentPriorityOrder;
        var prevDate = DateTime.Today;

        foreach (var escalation in escalationList)
        {
            var targetPriority = priorities.FirstOrDefault(p => p.Id == escalation.TargetPriorityId);
            if (targetPriority == null) continue;

            if (targetPriority.Order >= prevPriorityOrder)
            {
                errors.Add($"Нельзя задать повышение приоритета до \"{targetPriority.Name}\" — он не выше предыдущего.");
            }

            if (escalation.EscalationDate.HasValue && escalation.EscalationDate.Value.Date < DateTime.Today)
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

    public static ValidationResult ValidateRelations(IEnumerable<RelationDto>? relations, TaskItem task, List<PriorityLevel> priorities)
    {
        var errors = new List<string>();
        var relationList = relations?.Where(r => r.TargetTask != null).ToList() ?? [];

        foreach (var relation in relationList)
        {
            if (relation.Type is Core.Enums.RelationType.Blocks or Core.Enums.RelationType.BlockedBy)
            {
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

                var blockerDate = blocker.UserAssignedDate ?? blocker.ActualAssignedDate;
                var blockedDate = blocked.UserAssignedDate ?? blocked.ActualAssignedDate;

                if (blockerDate.HasValue && blockedDate.HasValue)
                {
                    if (blockerDate.Value.Date > blockedDate.Value.Date)
                    {
                        errors.Add($"Дата задачи \"{blocker.Title}\" ({blockerDate.Value:dd.MM.yyyy}) позже даты задачи, которую она блокирует ({blockedDate.Value:dd.MM.yyyy})");
                    }
                }

                var blockerPriorityOrder = blocker.PriorityId.HasValue
                    ? priorities.FirstOrDefault(p => p.Id == blocker.PriorityId)?.Order ?? 99
                    : 99;
                var blockedPriorityOrder = blocked.PriorityId.HasValue
                    ? priorities.FirstOrDefault(p => p.Id == blocked.PriorityId)?.Order ?? 99
                    : 99;

                if (blockerPriorityOrder > blockedPriorityOrder)
                {
                    var blockerPriority = priorities.FirstOrDefault(p => p.Id == blocker.PriorityId)?.Name ?? "Не задан";
                    var blockedPriority = priorities.FirstOrDefault(p => p.Id == blocked.PriorityId)?.Name ?? "Не задан";
                    errors.Add($"Приоритет задачи \"{blocker.Title}\" ({blockerPriority}) ниже приоритета связанной задачи ({blockedPriority})");
                }
            }

            if (HasCircularReference(relation, task))
            {
                errors.Add($"Обнаружена циклическая зависимость в отношении к задаче \"{relation.TargetTask!.Title}\"");
            }
        }

        return new(errors.Count == 0, errors);
    }

    private static bool HasCircularReference(RelationDto newRelation, TaskItem task)
    {
        if (newRelation.TargetTask == null) return false;
        if (newRelation.TargetTask.Id == task.Id) return true; // self-loop

        // Выполним обход в ширину от целевой задачи по исходящим связям (тип Blocks).
        // Считаем циклом только путь длины >= 2. Это позволит игнорировать прямые зеркальные
        // отношения между двумя задачами (когда одна задача явно блокирует другую и в ответ
        // есть зеркальная связь) — такие пары не должны считаться циклом здесь.

        var visited = new HashSet<int>();
        var queue = new Queue<(TaskItem node, int depth)>();

        visited.Add(newRelation.TargetTask.Id);

        // Начальные соседи — задачи, которые целевая задача блокирует (outgoing edges)
        var initialNeighbors = newRelation.TargetTask.Relations?.Where(r => r.Type == Core.Enums.RelationType.Blocks)
            .Select(r => r.TargetTask)
            .Where(t => t != null)
            .Cast<TaskItem>()
            .ToList() ?? [];

        foreach (var n in initialNeighbors.Where(n => n.Id != task.Id))
        {
            visited.Add(n.Id);
            queue.Enqueue((n, 1));
        }

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();

            var neighbors = node.Relations?.Where(r => r.Type == Core.Enums.RelationType.Blocks)
                .Select(r => r.TargetTask)
                .Where(t => t != null)
                .Cast<TaskItem>() ?? [];

            foreach (var nbr in neighbors)
            {
                if (nbr.Id == task.Id)
                {
                    // нашли путь от target -> ... -> task длины >= 2
                    return true;
                }

                if (!visited.Add(nbr.Id)) continue;
                queue.Enqueue((nbr, depth + 1));
            }
        }

        return false;
    }
}