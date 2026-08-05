using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Validation;
using FlowFocus.Data.Services;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for recurring task generation algorithms, daily/monthly/yearly rules, subtask cascading, and idempotency.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Recurrence")]
[Collection("StaticState")]
public class RecurringTasksEngineTests : IntegrationTestBase
{
    /// <summary>
    /// Verifies that completing a daily task creates a new copy for tomorrow in repository.
    /// </summary>
    [Fact]
    public void CompleteDailyTask_CreatesNewCopyForTomorrowInRepository()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var task = new TaskItemBuilder()
            .WithId(100)
            .WithTitle("Daily Task")
            .WithScheduledDate(today, DateSource.AutoFixed)
            .WithRecurrence(RecurrenceType.Daily)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);

        // Act
        TaskRepo.CompleteTask(task.Id);

        var allTasks = TaskRepo.GetAll();
        var completedTask = TaskRepo.GetById(task.Id);
        var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == task.Id);

        // Assert
        completedTask!.Status.Should().Be(TaskStatus.Completed);
        newCopy.Should().NotBeNull();
        newCopy!.Title.Should().Be("Daily Task");
        newCopy.ScheduledDate.Should().Be(today.AddDays(1));
        newCopy.DateSource.Should().Be(DateSource.AutoFixed);
        newCopy.Status.Should().Be(TaskStatus.Planned);
    }

    /// <summary>
    /// Verifies that completing an overdue task calculates next recurrence date from actual completion date.
    /// </summary>
    [Fact]
    public void CompleteOverdueTask_CalculatesNextDateFromActualCompletionDate()
    {
        // Arrange
        var overdueDate = TodoDay.Today.Yesterday.AddDays(-2).ToDateTime();
        var task = new TaskItemBuilder()
            .WithId(200)
            .WithTitle("Overdue Daily Task")
            .WithScheduledDate(overdueDate, DateSource.AutoFixed)
            .WithRecurrence(RecurrenceType.Daily)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);

        // Act
        TaskRepo.CompleteTask(task.Id);

        var allTasks = TaskRepo.GetAll();
        var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == task.Id);

        // Assert
        newCopy.Should().NotBeNull();
        newCopy!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that completing a monthly task calculates next date in following month.
    /// </summary>
    [Fact]
    public void CompleteMonthlyTask_CreatesCopyNextMonth()
    {
        // Arrange
        TaskRecurrenceService recurrenceService = new();
        DateTime aug3 = new(2026, 8, 3);

        var task = new TaskItemBuilder()
            .WithId(300)
            .WithRecurrence(RecurrenceType.Monthly)
            .WithCompletedDate(aug3)
            .Build();

        // Act
        var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

        // Assert
        nextDate.Should().Be(new DateTime(2026, 9, 3));
    }

    /// <summary>
    /// Verifies that completing a Jan 31 monthly task calculates Feb 28 safely without invalid date errors.
    /// </summary>
    [Fact]
    public void CompleteJan31MonthlyTask_CalculatesFeb28WithoutInvalidDateError()
    {
        // Arrange
        TaskRecurrenceService recurrenceService = new();
        DateTime jan31 = new(2026, 1, 31);

        var task = new TaskItemBuilder()
            .WithId(301)
            .WithRecurrence(RecurrenceType.Monthly)
            .WithScheduledDate(jan31)
            .WithCompletedDate(jan31)
            .Build();

        // Act
        var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

        // Assert
        nextDate.Should().Be(new DateTime(2026, 2, 28));
    }

    /// <summary>
    /// Verifies that completing a yearly task calculates next date in following year.
    /// </summary>
    [Fact]
    public void CompleteYearlyTask_CreatesCopyNextYear()
    {
        // Arrange
        TaskRecurrenceService recurrenceService = new();
        DateTime aug3 = new(2026, 8, 3);

        var task = new TaskItemBuilder()
            .WithId(302)
            .WithRecurrence(RecurrenceType.Yearly)
            .WithScheduledDate(aug3)
            .WithCompletedDate(aug3)
            .Build();

        // Act
        var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

        // Assert
        nextDate.Should().Be(new DateTime(2027, 8, 3));
    }

    /// <summary>
    /// Verifies that completing a recurring task cascades subtasks to the newly created copy in repository.
    /// </summary>
    [Fact]
    public void CompleteRecurringTask_CascadesSubtasksToNewCopyInRepository()
    {
        // Arrange
        var (parent, _) = TaskItemBuilder.CreateParentWithSubtasks(2, 500);
        parent.IsRecurring = true;
        parent.RecurrenceType = RecurrenceType.Daily;
        parent.ScheduledDate = TodoDay.Today.ToDateTime();
        parent.Status = TaskStatus.Planned;

        TaskRepo.Add(parent);
        Context.ChangeTracker.Clear();

        // Act
        TaskRepo.CompleteTask(parent.Id);

        var allTasks = TaskRepo.GetAll();
        var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == parent.Id);

        // Assert
        newCopy.Should().NotBeNull();
        newCopy!.Subtasks.Should().HaveCount(2);
        newCopy.Subtasks.Select(s => s.Title).Should().ContainInConsecutiveOrder("Subtask 1", "Subtask 2");
    }
    
    /// <summary>
    /// Verifies that rapid double-clicks on completion generates only a single copy in repository.
    /// </summary>
    [Fact]
    public void RapidClicksOnCompletion_GeneratesOnlySingleCopy()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(500)
            .WithTitle("Rapid Click Task")
            .WithRecurrence(RecurrenceType.Daily)
            .WithScheduledDate(TodoDay.Today.ToDateTime())
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);

        // Act
        TaskRepo.CompleteTask(task.Id);
        TaskRepo.CompleteTask(task.Id);

        var copies = TaskRepo.GetAll().Where(t => t.RecurrenceSourceId == task.Id).ToList();

        // Assert
        copies.Should().HaveCount(1);
    }

    [Fact]
    public void RecurringTasks_CannotBeAssigned_AutoFlexibleDateSource()
    {
        // Arrange
        var recurringTask = new TaskItemBuilder()
            .WithRecurrence(RecurrenceType.Daily)
            .WithScheduledDate(DateTime.UtcNow, DateSource.AutoFixed)
            .Build();

        // Act
        var act = () =>
        {
            recurringTask.DateSource = DateSource.AutoFlexible;
            TaskItemValidator.ValidateRecurringTaskCreation(recurringTask);
        };

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*не может быть автоматически гибкой*");
    }
}
