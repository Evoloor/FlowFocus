using FlowFocus.Core.Models;

namespace FlowFocus.Blazor.Dialogs.Validators;

public static class TaskEditValidator
{
    public record ValidationResult(bool IsValid, List<string> Errors);

    public static ValidationResult ValidateEscalations(IEnumerable<EscalationDto>? escalations, TaskItem task, List<PriorityLevel> priorities)
    {
        var errors = new List<string>();
        var escalationList = escalations?.Where(e => e.EscalationDate != null).OrderBy(e => e.EscalationDate).ToList() ?? new List<EscalationDto>();

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
                errors.Add($"��������� ���������� ������ ���� ����������������: {targetPriority.Name} �� ���� ��������");
            }

            if (escalation.EscalationDate.HasValue && escalation.EscalationDate.Value.Date < DateTime.Today)
            {
                errors.Add($"���� ��������� {escalation.EscalationDate.Value:dd.MM.yyyy} ��� ������");
            }

            if (escalation.EscalationDate.HasValue && escalation.EscalationDate.Value.Date <= prevDate)
            {
                errors.Add("���� ��������� ������ ���� �����������������");
            }

            prevPriorityOrder = targetPriority.Order;
            prevDate = escalation.EscalationDate?.Date ?? prevDate;
        }

        return new ValidationResult(!errors.Any(), errors);
    }

    public static ValidationResult ValidateRelations(IEnumerable<RelationDto>? relations, TaskItem task, List<PriorityLevel> priorities)
    {
        var errors = new List<string>();
        var relationList = relations?.Where(r => r.TargetTask != null).ToList() ?? new List<RelationDto>();

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
                        errors.Add($"���� ������� \"{blocker.Title}\" ({blockerDate.Value:dd.MM.yyyy}) ������ ���� �� ����� ���� ����������� ������ ({blockedDate.Value:dd.MM.yyyy})");
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
                    var blockerPriority = priorities.FirstOrDefault(p => p.Id == blocker.PriorityId)?.Name ?? "�� �����";
                    var blockedPriority = priorities.FirstOrDefault(p => p.Id == blocked.PriorityId)?.Name ?? "�� �����";
                    errors.Add($"��������� ������� \"{blocker.Title}\" ({blockerPriority}) �� ������ ���� ������ ����������� ������ ({blockedPriority})");
                }
            }

            if (HasCircularReference(relation, task))
            {
                errors.Add($"���������� ����������� ����������� � ������� \"{relation.TargetTask!.Title}\"");
            }
        }

        return new ValidationResult(!errors.Any(), errors);
    }

    private static bool HasCircularReference(RelationDto newRelation, TaskItem task)
    {
        if (newRelation.TargetTask == null) return false;
        if (newRelation.TargetTask.Id == task.Id) return true;

        var targetRelations = newRelation.TargetTask.Relations ?? new List<TaskRelation>();
        foreach (var r in targetRelations)
        {
            if (r.TargetTaskId == task.Id &&
                ((r.Type == Core.Enums.RelationType.Blocks && newRelation.Type == Core.Enums.RelationType.BlockedBy) ||
                 (r.Type == Core.Enums.RelationType.BlockedBy && newRelation.Type == Core.Enums.RelationType.Blocks)))
            {
                return true;
            }
        }

        return false;
    }
}