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
        var allEscalations = escalations?.ToList() ?? [];

        if (allEscalations.Any(e => !e.IsApplied && e.EscalationDate == null))
        {
            errors.Add("Укажите дату повышения приоритета.");
        }

        var activeEscalations = allEscalations
            .Where(e => !e.IsApplied && e.EscalationDate != null)
            .OrderBy(e => e.EscalationDate)
            .ToList();

        var currentPriorityOrder = task.PriorityId.HasValue
            ? priorities.FirstOrDefault(p => p.Id == task.PriorityId)?.Order ?? 99
            : 99;

        var prevPriorityOrder = currentPriorityOrder;
        var today = TodoDay.Today.ToDateTime();
        var prevDate = today;

        foreach (var escalation in activeEscalations)
        {
            var targetPriority = priorities.FirstOrDefault(p => p.Id == escalation.TargetPriorityId);
            if (targetPriority == null) continue;

            if (targetPriority.Order >= prevPriorityOrder)
            {
                errors.Add(
                    $"Нельзя задать повышение приоритета до \"{targetPriority.Name}\" — он не выше предыдущего.");
            }

            var escalationDate = escalation.EscalationDate!.Value.Date;
            if (escalationDate < today)
            {
                errors.Add($"Дата повышения {escalationDate:dd.MM.yyyy} уже в прошлом");
            }
            else if (escalationDate <= prevDate)
            {
                errors.Add(prevDate == today
                    ? "Дата повышения должна быть не раньше завтрашнего дня."
                    : "Дата повышения должна быть позже предыдущей даты.");
            }

            prevPriorityOrder = targetPriority.Order;
            prevDate = escalationDate;
        }

        return new(errors.Count == 0, errors);
    }
}
