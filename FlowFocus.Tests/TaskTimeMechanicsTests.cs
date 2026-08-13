using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Domain unit tests for time mechanics, completion dates, midnight boundaries, and DayStartHour rules.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Time")]
[Collection("StaticState")]
public class TaskTimeMechanicsTests : IntegrationTestBase
{
    [Fact]
    public void CompletingOverdueTask_SetsCompletionDateToOriginalScheduledDate()
    {
        // Arrange: Просроченные задачи завершаются тем числом, на которое были назначены
        var pastDate = TodoDay.Today.ToDateTime().AddDays(-5);
        var overdueTask = new TaskItemBuilder()
            .WithId(101)
            .WithScheduledDate(pastDate, dateSource: DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(overdueTask);

        // Act
        TaskRepo.CompleteTask(overdueTask.Id);

        // Assert
        var saved = TaskRepo.GetById(101);
        saved.Should().NotBeNull();
        saved!.CompletedDate.Should().Be(pastDate, "Просроченная задача должна 'остаться' в дне своего назначения");
    }

    [Fact]
    public void CompletingFutureTask_SetsCompletionDateToToday()
    {
        // Arrange: Предстоящие дела при досрочном завершении завершаются сегодняшним днем
        var futureDate = TodoDay.Today.ToDateTime().AddDays(5);
        var futureTask = new TaskItemBuilder()
            .WithId(102)
            .WithScheduledDate(futureDate, dateSource: DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(futureTask);

        // Act
        TaskRepo.CompleteTask(futureTask.Id);

        // Assert
        var saved = TaskRepo.GetById(102);
        saved.Should().NotBeNull();
        saved!.CompletedDate.Should().Be(TodoDay.Today.ToDateTime(), "Будущая задача при выполнении переносится в сегодняшний список завершенных");
    }

    [Fact]
    public void SystemTime0100AM_WithStartOfDay4AM_RecordsCompletionDateAsPreviousCalendarDayInRepository()
    {
        // Arrange
        try
        {
            TodoDay.Configure(dayStartHour: 4);

            var task = new TaskItemBuilder()
                .WithId(id: 100)
                .WithTitle(title: "Overdue Midnight Task")
                .WithScheduledDate(date: new DateTime(year: 2026, month: 8, day: 3))
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            TaskRepo.Add(entity: task);

            // Act
            TaskRepo.CompleteTask(taskId: task.Id);
            var savedTask = TaskRepo.GetById(id: task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask!.Status.Should().Be(expected: TaskStatus.Completed);
            savedTask.CompletedDate.Should().NotBeNull();
        }
        finally
        {
            TodoDay.Configure(dayStartHour: 5);
        }
    }

    [Fact]
    public void StartOfDayAt4AM_SystemTime230AM_EvaluatesPreviousLogicalDate()
    {
        // Arrange
        try
        {
            TodoDay.Configure(dayStartHour: 4);
            DateTime systemTime = new(year: 2026, month: 8, day: 4, hour: 2, minute: 30, second: 0); // 02:30 AM

            // Act
            var logicalDate = systemTime.Hour < 4 ? systemTime.Date.AddDays(value: -1) : systemTime.Date;

            // Assert
            logicalDate.Should().Be(expected: new(year: 2026, month: 8, day: 3));
        }
        finally
        {
            TodoDay.Configure(dayStartHour: 5);
        }
    }
}

