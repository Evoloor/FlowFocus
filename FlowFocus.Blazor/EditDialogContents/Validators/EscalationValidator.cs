using FlowFocus.Core;
using FlowFocus.Core.Models;
using static FlowFocus.Blazor.EditDialogContents.Validators.TaskEditValidator;

namespace FlowFocus.Blazor.EditDialogContents.Validators;

public static class EscalationValidator
{
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
}
