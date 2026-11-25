using System.Globalization;
using FlowFocus.Core.Enums;

namespace FlowFocus.Core.Helpers;

/// <summary>
/// Единый хелпер форматирования длительности и значений метрик дашборда
/// </summary>
public static class DurationFormatHelper
{
    /// <summary>
    /// Форматирует длительность в минутах (например: "45 мин", "2 ч", "2 ч 15 мин").
    /// </summary>
    public static string FormatMinutes(int minutes)
    {
        if (minutes <= 0) return "0 мин";
        if (minutes < 60) return $"{minutes} мин";
        var hours = minutes / 60;
        var mins = minutes % 60;
        return mins > 0 ? $"{hours} ч {mins} мин" : $"{hours} ч";
    }

    /// <summary>
    /// Округляет дробное число минут и форматирует длительность.
    /// </summary>
    public static string FormatMinutes(double minutes)
    {
        var rounded = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        return FormatMinutes(rounded);
    }

    /// <summary>
    /// Форматирует знаковую разницу времени (например: "+1 ч 15 мин", "-30 мин", "0 мин").
    /// </summary>
    public static string FormatSignedMinutes(int deltaMinutes)
    {
        if (deltaMinutes == 0) return "0 мин";
        return deltaMinutes > 0
            ? $"+{FormatMinutes(deltaMinutes)}"
            : $"-{FormatMinutes(-deltaMinutes)}";
    }

    /// <summary>
    /// Форматирует целочисленное значение метрики в соответствии с режимом агрегации.
    /// </summary>
    public static string FormatMetricValue(int value, DashboardAggregationMode mode) =>
        mode == DashboardAggregationMode.TimeMinutes
            ? FormatMinutes(value)
            : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Определяет компактный масштаб отображения длительности для блока по максимальному значению минут.
    /// </summary>
    public static DurationDisplayScale ResolveCompactScale(double maxMinutes) =>
        maxMinutes >= 60 ? DurationDisplayScale.CompactHours : DurationDisplayScale.CompactMinutes;

    /// <summary>
    /// Определяет компактный масштаб отображения длительности по набору целочисленных значений (в минутах).
    /// </summary>
    public static DurationDisplayScale ResolveCompactScale(IEnumerable<int> values) =>
        ResolveCompactScale(values?.Select(v => (double)v).DefaultIfEmpty(0).Max() ?? 0);

    /// <summary>
    /// Определяет компактный масштаб отображения длительности по набору вещественных значений (в минутах).
    /// </summary>
    public static DurationDisplayScale ResolveCompactScale(IEnumerable<double> values) =>
        ResolveCompactScale(values?.DefaultIfEmpty(0).Max() ?? 0);

    /// <summary>
    /// Форматирует длительность в соответствии с выбранным масштабом.
    /// </summary>
    public static string FormatCompact(double minutes, DurationDisplayScale scale, string zeroLabel = "0")
    {
        if (minutes <= 0) return zeroLabel;

        if (scale == DurationDisplayScale.Detailed)
        {
            return FormatMinutes(minutes);
        }

        if (scale == DurationDisplayScale.CompactMinutes)
        {
            var roundedMins = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
            return $"{roundedMins} мин";
        }

        var hours = minutes / 60.0;
        if (hours >= 100)
        {
            var roundedWholeHours = Math.Round(hours, MidpointRounding.AwayFromZero);
            return $"{roundedWholeHours.ToString("F0", CultureInfo.InvariantCulture)} ч";
        }

        if (hours < 0.05)
        {
            return "<0.1 ч";
        }

        var roundedHours = Math.Round(hours, 1, MidpointRounding.AwayFromZero);
        return $"{roundedHours.ToString("0.#", CultureInfo.InvariantCulture)} ч";
    }

    /// <summary>
    /// Форматирует целочисленное значение метрики в компактном виде в соответствии с режимом агрегации.
    /// </summary>
    public static string FormatCompactMetricValue(
        int value,
        DashboardAggregationMode mode,
        DurationDisplayScale scale,
        string zeroLabel = "0") =>
        mode == DashboardAggregationMode.TimeMinutes
            ? FormatCompact(value, scale, zeroLabel)
            : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Форматирует вещественное значение метрики в соответствии с режимом агрегации.
    /// </summary>
    public static string FormatMetricValue(double value, DashboardAggregationMode mode)
    {
        if (mode == DashboardAggregationMode.TimeMinutes)
        {
            return FormatMinutes(value);
        }

        return value % 1 == 0
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : value.ToString("F1", CultureInfo.InvariantCulture);
    }
}
