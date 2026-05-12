namespace FlowFocus.Core;

/// <summary>
/// Хелпер для работы с датами с учётом времени начала дня
/// </summary>
public static class DateHelper
{
    /// <summary>
    /// Получить "логическую" дату с учётом времени начала дня.
    /// Если текущее время меньше времени начала дня, возвращает вчерашнюю дату.
    /// </summary>
    private static DateTime GetLogicalDate(DateTime dateTime, int dayStartHour)
    {
        if (dateTime.Hour >= 0 && dateTime.Hour < dayStartHour)
        {
            return dateTime.Date.AddDays(-1);
        }
        return dateTime.Date;
    }

    /// <summary>
    /// Получить сегодняшнюю "логическую" дату
    /// </summary>
    public static DateTime GetLogicalToday(int dayStartHour)
    {
        return GetLogicalDate(DateTime.Now, dayStartHour);
    }

    /// <summary>
    /// Проверить, является ли дата просроченной
    /// </summary>
    public static bool IsOverdue(DateTime? date, int dayStartHour)
    {
        if (date == null) return false;
        var logicalToday = GetLogicalToday(dayStartHour);
        return date.Value < logicalToday;
    }

    /// <summary>
    /// Получить завтрашнюю "логическую" дату с учётом времени начала дня
    /// </summary>
    public static DateTime GetTomorrow(int dayStartHour)
    {
        return GetLogicalToday(dayStartHour).AddDays(1);
    }

    /// <summary>
    /// Проверить, является ли дата "сегодняшней" (логически)
    /// </summary>
    public static bool IsToday(DateTime? date, int dayStartHour)
    {
        if (date == null) return false;
        return date.Value.Date == GetLogicalToday(dayStartHour);
    }

    /// <summary>
    /// Проверить, является ли дата "завтрашней" (логически)
    /// </summary>
    public static bool IsTomorrow(DateTime? date, int dayStartHour)
    {
        if (date == null) return false;
        return date.Value.Date == GetTomorrow(dayStartHour);
    }
}