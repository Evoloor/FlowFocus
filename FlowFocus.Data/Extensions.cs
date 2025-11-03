namespace FlowFocus.Data;

public static class Extensions
{
    public static DateTime StartOfToday(this DateTime date, TimeSpan? dayStartTime = null)
    {
        var hour = dayStartTime?.Hours ?? 5;
        var baseDate = date.Hour < hour ? date.Date.AddDays(-1) : date.Date;
        return baseDate.AddHours(hour);
    }

    public static DateTime StartOfTomorrow(this DateTime date, TimeSpan? dayStartTime = null)
        => date.StartOfToday(dayStartTime).AddDays(1);
}