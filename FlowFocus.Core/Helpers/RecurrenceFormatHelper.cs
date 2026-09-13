using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;

namespace FlowFocus.Core.Helpers;

/// <summary>
/// Хелпер форматирования строк повторения задач для UI
/// </summary>
public static class RecurrenceFormatHelper
{
    private static readonly (int Bit, string ShortName, string SingleText)[] DayMappings =
    [
        (1, "Пн", "Каждый понедельник"),
        (2, "Вт", "Каждый вторник"),
        (4, "Ср", "Каждую среду"),
        (8, "Чт", "Каждый четверг"),
        (16, "Пт", "Каждую пятницу"),
        (32, "Сб", "Каждую субботу"),
        (64, "Вс", "Каждое воскресенье")
    ];

    public static string GetRecurrenceText(TaskItem? task)
    {
        if (task == null || !task.IsRecurring || task.RecurrenceType == RecurrenceType.None)
            return string.Empty;

        return task.RecurrenceType switch
        {
            RecurrenceType.EveryN => FormatEveryN(task.RecurrenceUnit, task.RecurrenceInterval ?? 1),
            RecurrenceType.WeekDays => FormatWeekDays(task.RecurrenceWeekDays ?? 0),
            _ => string.Empty
        };
    }

    public static string FormatEveryN(RecurrenceUnit unit, int interval)
    {
        var n = interval <= 0 ? 1 : interval;

        return unit switch
        {
            RecurrenceUnit.Days => n switch
            {
                1 => "Ежедневно",
                7 => "Еженедельно",
                _ => $"Каждые {n} дн"
            },
            RecurrenceUnit.Months => n switch
            {
                1 => "Ежемесячно",
                _ => $"Каждые {n} мес"
            },
            RecurrenceUnit.Years => n switch
            {
                1 => "Ежегодно",
                _ => FormatYears(n)
            },
            _ => $"Каждые {n} дн"
        };
    }

    private static string FormatYears(int n)
    {
        var mod100 = n % 100;
        var mod10 = mod100 % 10;
        if (mod100 is >= 11 and <= 19) return $"Каждые {n} лет";
        if (mod10 == 1) return $"Каждый {n} год";
        if (mod10 is >= 2 and <= 4) return $"Каждые {n} года";
        return $"Каждые {n} лет";
    }

    public static string FormatWeekDays(int mask)
    {
        if (mask == 0) return "По дням недели";

        var selected = DayMappings.Where(d => (mask & d.Bit) != 0).ToList();

        if (selected.Count == 0) return "По дням недели";

        if (selected.Count == 1)
        {
            return selected[0].SingleText;
        }

        return string.Join(", ", selected.Select(d => d.ShortName));
    }
}
