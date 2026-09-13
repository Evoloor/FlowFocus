using FlowFocus.Core.Helpers;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Data.Services;
using FluentAssertions;
using Xunit;

namespace FlowFocus.Tests;

public class RecurrenceEngineAndUiTests : IntegrationTestBase
{
    [Theory]
    [InlineData(RecurrenceUnit.Days, 1, "Ежедневно")]
    [InlineData(RecurrenceUnit.Days, 7, "Еженедельно")]
    [InlineData(RecurrenceUnit.Days, 3, "Каждые 3 дн")]
    [InlineData(RecurrenceUnit.Months, 1, "Ежемесячно")]
    [InlineData(RecurrenceUnit.Months, 2, "Каждые 2 мес")]
    [InlineData(RecurrenceUnit.Years, 1, "Ежегодно")]
    [InlineData(RecurrenceUnit.Years, 2, "Каждые 2 года")]
    [InlineData(RecurrenceUnit.Years, 5, "Каждые 5 лет")]
    [InlineData(RecurrenceUnit.Years, 21, "Каждый 21 год")]
    public void FormatEveryN_FormatsCorrectly(RecurrenceUnit unit, int interval, string expected)
    {
        var text = RecurrenceFormatHelper.FormatEveryN(unit, interval);
        text.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "Каждый понедельник")]
    [InlineData(2, "Каждый вторник")]
    [InlineData(4, "Каждую среду")]
    [InlineData(8, "Каждый четверг")]
    [InlineData(16, "Каждую пятницу")]
    [InlineData(32, "Каждую субботу")]
    [InlineData(64, "Каждое воскресенье")]
    public void FormatWeekDays_SingleDay_MatchesGrammaticalGender(int mask, string expected)
    {
        var text = RecurrenceFormatHelper.FormatWeekDays(mask);
        text.Should().Be(expected);
    }

    [Fact]
    public void FormatWeekDays_MultipleDays_ListsCommaSeparated()
    {
        // 2 (Вт) + 8 (Чт) = 10
        RecurrenceFormatHelper.FormatWeekDays(10).Should().Be("Вт, Чт");

        // 1 (Пн) + 4 (Ср) + 16 (Пт) = 21
        RecurrenceFormatHelper.FormatWeekDays(21).Should().Be("Пн, Ср, Пт");
    }

    [Fact]
    public void TaskItem_NormalizesLegacyRecurrenceTypes()
    {
        // Legacy 1: Daily -> EveryN, Days, 1
        var taskDaily = new TaskItem { RecurrenceType = (RecurrenceType)1 };
        taskDaily.RecurrenceType.Should().Be(RecurrenceType.EveryN);
        taskDaily.RecurrenceUnit.Should().Be(RecurrenceUnit.Days);
        taskDaily.RecurrenceInterval.Should().Be(1);

        // Legacy 2: EveryNDays -> EveryN, Days
        var taskEveryNDays = new TaskItem { RecurrenceInterval = 4, RecurrenceType = (RecurrenceType)2 };
        taskEveryNDays.RecurrenceType.Should().Be(RecurrenceType.EveryN);
        taskEveryNDays.RecurrenceUnit.Should().Be(RecurrenceUnit.Days);
        taskEveryNDays.RecurrenceInterval.Should().Be(4);

        // Legacy 4: Monthly -> EveryN, Months
        var taskMonthly = new TaskItem { RecurrenceInterval = 2, RecurrenceType = (RecurrenceType)4 };
        taskMonthly.RecurrenceType.Should().Be(RecurrenceType.EveryN);
        taskMonthly.RecurrenceUnit.Should().Be(RecurrenceUnit.Months);
        taskMonthly.RecurrenceInterval.Should().Be(2);

        // Legacy 5: Yearly -> EveryN, Years
        var taskYearly = new TaskItem { RecurrenceInterval = 3, RecurrenceType = (RecurrenceType)5 };
        taskYearly.RecurrenceType.Should().Be(RecurrenceType.EveryN);
        taskYearly.RecurrenceUnit.Should().Be(RecurrenceUnit.Years);
        taskYearly.RecurrenceInterval.Should().Be(3);
    }

    [Fact]
    public void StorageContext_EnforcesRecurrenceIntervalMinimumOneForEveryN()
    {
        var task = new TaskItem
        {
            Title = "Task with 0 interval",
            IsRecurring = true,
            RecurrenceType = RecurrenceType.EveryN,
            RecurrenceUnit = RecurrenceUnit.Days,
            RecurrenceInterval = 0
        };

        Context.Tasks.Add(task);
        Context.SaveChanges();

        task.RecurrenceInterval.Should().Be(1);
    }

    [Fact]
    public void TaskRecurrenceService_CalculatesEveryNNextDatesCorrectly()
    {
        var recurrenceService = new TaskRecurrenceService();
        var baseDate = new DateTime(2026, 9, 13);

        // 1 day
        var task1Day = new TaskItem
        {
            IsRecurring = true,
            RecurrenceType = RecurrenceType.EveryN,
            RecurrenceUnit = RecurrenceUnit.Days,
            RecurrenceInterval = 1
        };
        recurrenceService.CalculateNextRecurrenceDateFromBase(task1Day, baseDate)
            .Should().Be(new DateTime(2026, 9, 14));

        // 7 days
        var task7Days = new TaskItem
        {
            IsRecurring = true,
            RecurrenceType = RecurrenceType.EveryN,
            RecurrenceUnit = RecurrenceUnit.Days,
            RecurrenceInterval = 7
        };
        recurrenceService.CalculateNextRecurrenceDateFromBase(task7Days, baseDate)
            .Should().Be(new DateTime(2026, 9, 20));

        // 1 month
        var task1Month = new TaskItem
        {
            IsRecurring = true,
            RecurrenceType = RecurrenceType.EveryN,
            RecurrenceUnit = RecurrenceUnit.Months,
            RecurrenceInterval = 1
        };
        recurrenceService.CalculateNextRecurrenceDateFromBase(task1Month, baseDate)
            .Should().Be(new DateTime(2026, 10, 13));

        // 1 year
        var task1Year = new TaskItem
        {
            IsRecurring = true,
            RecurrenceType = RecurrenceType.EveryN,
            RecurrenceUnit = RecurrenceUnit.Years,
            RecurrenceInterval = 1
        };
        recurrenceService.CalculateNextRecurrenceDateFromBase(task1Year, baseDate)
            .Should().Be(new DateTime(2027, 9, 13));
    }
}
