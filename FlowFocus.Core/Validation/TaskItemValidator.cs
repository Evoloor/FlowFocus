using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;

namespace FlowFocus.Core.Validation;

/// <summary>
/// Валидатор свойств модели задачи
/// </summary>
public static class TaskItemValidator
{
    public static bool IsTitleValid(string? title)
    {
        return !string.IsNullOrWhiteSpace(title);
    }

    public static int ClampInterest(int? interest)
    {
        if (!interest.HasValue) return 5;
        return Math.Clamp(interest.Value, 1, 10);
    }

    public static int ClampComplexity(int? complexity)
    {
        if (!complexity.HasValue) return 1;
        return Math.Clamp(complexity.Value, 1, 100);
    }

    public static int ClampEstimatedMinutes(int? minutes)
    {
        if (!minutes.HasValue) return 15;
        return Math.Clamp(minutes.Value, 1, 10000);
    }

    public static void ValidateRecurringTaskCreation(TaskItem task)
    {
        if (task is { IsRecurring: true, ScheduledDate: null })
        {
            throw new InvalidOperationException("Повторяющаяся задача при первом создании обязана иметь ручную дату.");
        }

        if (task is { IsRecurring: true, DateSource: DateSource.AutoFlexible })
        {
            throw new InvalidOperationException("Повторяющаяся задача не может быть автоматически гибкой.");
        }
    }

    public static void ValidateEscalationRule(PriorityLevel currentPriority, PriorityLevel targetPriority)
    {
        ArgumentNullException.ThrowIfNull(currentPriority);
        ArgumentNullException.ThrowIfNull(targetPriority);

        if (targetPriority.Order >= currentPriority.Order)
        {
            throw new InvalidOperationException("Правило повышения доступно только для более высоких приоритетов.");
        }
    }
}
