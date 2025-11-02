namespace FlowFocus.Core.Utility;

public static class Extensions
{
    public static DateTime StartOfToday(this DateTime date)
    {
        var hour = AppStateManager.Shared.State.Settings.DayStartTime.Hours;
        var baseDate = date.Hour < hour ? date.Date.AddDays(-1) : date.Date;
        return baseDate.AddHours(hour);
    }

    public static DateTime StartOfTomorrow(this DateTime date)
        => date.StartOfToday().AddDays(1);
}