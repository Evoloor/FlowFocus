using FlowFocus.Core.Models;

namespace FlowFocus.Tests.Builders;

public class UserSettingsBuilder : EntityBuilder<UserSettings, UserSettingsBuilder>
{
    private int _dayStartHour = 5;
    private int _dailyComplexityLimit = 100;
    private int _dailyTimeLimit = 480;
    private int _dailyTaskLimit = 10;
    private const bool AutoDistributeEnabled = true;
    private const bool IsDarkMode = true;
    private bool _hideTaskTitlesDefault;
    private int _defaultPriorityId = 3;

    public UserSettingsBuilder WithDayStartHour(int hour)
    {
        _dayStartHour = hour;
        return this;
    }

    public UserSettingsBuilder WithDailyComplexityLimit(int limit)
    {
        _dailyComplexityLimit = limit;
        return this;
    }

    public UserSettingsBuilder WithDailyTimeLimit(int limit)
    {
        _dailyTimeLimit = limit;
        return this;
    }

    public UserSettingsBuilder WithDailyTaskLimit(int limit)
    {
        _dailyTaskLimit = limit;
        return this;
    }

    public UserSettingsBuilder WithHideTaskTitlesDefault(bool hide)
    {
        _hideTaskTitlesDefault = hide;
        return this;
    }

    public UserSettingsBuilder WithDefaultPriorityId(int priorityId)
    {
        _defaultPriorityId = priorityId;
        return this;
    }

    public override UserSettings Build()
    {
        return new()
        {
            Id = Id,
            DayStartHour = _dayStartHour,
            DailyComplexityLimit = _dailyComplexityLimit,
            DailyTimeLimit = _dailyTimeLimit,
            DailyTaskLimit = _dailyTaskLimit,
            AutoDistributeEnabled = AutoDistributeEnabled,
            IsDarkMode = IsDarkMode,
            HideTaskTitlesDefault = _hideTaskTitlesDefault,
            DefaultPriorityId = _defaultPriorityId
        };
    }
}
