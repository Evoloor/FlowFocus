using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
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
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
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
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
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
    /// Verifies that completing a task with a manual future date calculates next date relative to completion day (today), not future date.
    /// </summary>
    [Fact]
    public void CompleteTask_WithManualFutureDate_CalculatesNextDateRelativeToToday()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var futureDate = today.AddDays(5);
        var task = new TaskItemBuilder()
            .WithId(201)
            .WithTitle("Future Manual Recurring Task")
            .WithScheduledDate(futureDate, DateSource.Manual)
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);

        // Act
        TaskRepo.CompleteTask(task.Id);

        var allTasks = TaskRepo.GetAll();
        var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == task.Id);

        // Assert
        newCopy.Should().NotBeNull();
        newCopy!.ScheduledDate.Should().Be(today.AddDays(1));
        newCopy.DateSource.Should().Be(DateSource.AutoFixed);
    }

    /// <summary>
    /// Verifies that completing an EveryNDays task with a future scheduled date calculates next date relative to today.
    /// </summary>
    [Fact]
    public void CompleteTask_WithEveryNDaysAndFutureDate_CalculatesNextDateRelativeToToday()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var futureDate = today.AddDays(10);
        var task = new TaskItemBuilder()
            .WithId(202)
            .WithTitle("Every 3 Days Future Task")
            .WithScheduledDate(futureDate, DateSource.AutoFixed)
            .WithRecurrence(RecurrenceType.EveryN, interval: 3, unit: RecurrenceUnit.Days)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);

        // Act
        TaskRepo.CompleteTask(task.Id);

        var allTasks = TaskRepo.GetAll();
        var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == task.Id);

        // Assert
        newCopy.Should().NotBeNull();
        newCopy!.ScheduledDate.Should().Be(today.AddDays(3));
        newCopy.DateSource.Should().Be(DateSource.AutoFixed);
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
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Months)
            .WithCompletedDate(aug3)
            .Build();

        // Act
        var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

        // Assert
        nextDate.Should().Be(new(2026, 9, 3));
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
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Months)
            .WithScheduledDate(jan31)
            .WithCompletedDate(jan31)
            .Build();

        // Act
        var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

        // Assert
        nextDate.Should().Be(new(2026, 2, 28));
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
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Years)
            .WithScheduledDate(aug3)
            .WithCompletedDate(aug3)
            .Build();

        // Act
        var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

        // Assert
        nextDate.Should().Be(new(2027, 8, 3));
    }

    /// <summary>
    /// Verifies that completing a recurring task cascades subtasks to the newly created copy in repository.
    /// </summary>
    [Fact]
    public void CompleteRecurringTask_CascadesSubtasksToNewCopyInRepository()
    {
        // Arrange
        (var parent, _) = TaskItemBuilder.CreateParentWithSubtasks(2, 500);
        parent.IsRecurring = true;
        parent.RecurrenceType = RecurrenceType.EveryN;
        parent.RecurrenceUnit = RecurrenceUnit.Days;
        parent.RecurrenceInterval = 1;
        parent.ScheduledDate = TodoDay.Today.ToDateTime();
        parent.DateSource = DateSource.AutoFixed;
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
    /// Verifies that completing a recurring parent task creates a new copy with planned subtasks,
    /// copying all subtask values (time, complexity, interest, flags, etc.) and live-binding their date to the new instance.
    /// </summary>
    [Fact]
    public void CompleteRecurringTask_NewInstanceCreatesPlannedSubtasksWithExactValues()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var subtask1 = new SubtaskItemBuilder()
            .WithId(701)
            .WithTitle("Subtask 1")
            .WithDescription("Subtask 1 description")
            .WithEstimatedMinutes(45)
            .WithComplexity(15)
            .WithInterest(8)
            .WithFavorite(true)
            .Build();

        var subtask2 = new SubtaskItemBuilder()
            .WithId(702)
            .WithTitle("Subtask 2")
            .WithDescription("Subtask 2 description")
            .WithEstimatedMinutes(20)
            .WithComplexity(5)
            .WithInterest(6)
            .WithHideUnderSpoiler(true)
            .Build();

        var recurringParent = new TaskItemBuilder()
            .WithId(700)
            .WithTitle("Recurring Parent")
            .WithScheduledDate(today, DateSource.AutoFixed)
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
            .WithStatus(TaskStatus.Planned)
            .WithSubtask(subtask1)
            .WithSubtask(subtask2)
            .Build();

        TaskRepo.Add(recurringParent);
        Context.ChangeTracker.Clear();

        // Act - Complete the recurring parent task
        TaskRepo.CompleteTask(recurringParent.Id);

        // Assert - Original task is completed
        var completedOriginal = TaskRepo.GetById(recurringParent.Id);
        completedOriginal.Should().NotBeNull();
        completedOriginal!.Status.Should().Be(TaskStatus.Completed);

        // Assert - New copy created for tomorrow
        var allTasks = TaskRepo.GetAll();
        var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == recurringParent.Id);
        newCopy.Should().NotBeNull();
        newCopy!.ScheduledDate.Should().Be(today.AddDays(1));
        newCopy.Status.Should().Be(TaskStatus.Planned);

        // Assert - Subtasks in new copy are cloned with Planned status and exact values
        newCopy.Subtasks.Should().HaveCount(2);

        var newSub1 = newCopy.Subtasks.FirstOrDefault(s => s.Title == "Subtask 1");
        newSub1.Should().NotBeNull();
        newSub1!.Status.Should().Be(TaskStatus.Planned, "Recurring subtask in new copy must reset to Planned");
        newSub1.Description.Should().Be("Subtask 1 description");
        newSub1.EstimatedMinutes.Should().Be(45);
        newSub1.Complexity.Should().Be(15);
        newSub1.Interest.Should().Be(8);
        newSub1.IsFavorite.Should().BeTrue();
        newSub1.ParentTaskId.Should().Be(newCopy.Id);

        var newSub2 = newCopy.Subtasks.FirstOrDefault(s => s.Title == "Subtask 2");
        newSub2.Should().NotBeNull();
        newSub2!.Status.Should().Be(TaskStatus.Planned, "Recurring subtask in new copy must reset to Planned");
        newSub2.Description.Should().Be("Subtask 2 description");
        newSub2.EstimatedMinutes.Should().Be(20);
        newSub2.Complexity.Should().Be(5);
        newSub2.Interest.Should().Be(6);
        newSub2.HideUnderSpoiler.Should().BeTrue();
        newSub2.ParentTaskId.Should().Be(newCopy.Id);
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
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
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
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
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

    /// <summary>
    /// Verifies that completing a recurring task copies its external conditions to the newly created recurrence copy.
    /// </summary>
    [Fact]
    public void CompleteRecurringTask_CopiesExternalConditionsToNewCopy()
    {
        // Arrange
        var condition = ConditionRepo.GetOrCreate("Требуется интернет");
        var today = TodoDay.Today.ToDateTime();
        var task = new TaskItemBuilder()
            .WithId(600)
            .WithTitle("Daily Task With Condition")
            .WithScheduledDate(today, DateSource.AutoFixed)
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
            .WithStatus(TaskStatus.Planned)
            .Build();

        task.Conditions.Add(new() { TaskId = task.Id, ConditionId = condition.Id });

        TaskRepo.Add(task);
        Context.ChangeTracker.Clear();

        // Act
        TaskRepo.CompleteTask(task.Id);

        var allTasks = TaskRepo.GetAll();
        var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == task.Id);

        // Assert
        newCopy.Should().NotBeNull();
        newCopy!.Conditions.Should().ContainSingle(c => c.ConditionId == condition.Id);
    }

    [Fact]
    public void NormalizeDateSources_WhenRecurringTaskHasAutoFlexible_NormalizesToAutoFixed()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var task = new TaskItemBuilder()
            .WithId(700)
            .WithTitle("Recurring with flexible")
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
            .WithScheduledDate(today, DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        Context.Tasks.Add(task);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();

        // Act
        TaskRepo.NormalizeTaskDateSources();

        // Assert
        var normalized = TaskRepo.GetById(700);
        normalized.Should().NotBeNull();
        normalized!.DateSource.Should().Be(DateSource.AutoFixed);
        normalized.ScheduledDate.Should().NotBeNull();
    }

    [Fact]
    public void NormalizeDateSources_WhenRecurringTaskHasNullDate_AssignsTodayAndAutoFixed()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(701)
            .WithTitle("Recurring without date")
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
            .WithScheduledDate(null, DateSource.AutoFixed)
            .WithStatus(TaskStatus.Planned)
            .Build();

        Context.Tasks.Add(task);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();

        // Act
        TaskRepo.NormalizeTaskDateSources();

        // Assert
        var normalized = TaskRepo.GetById(701);
        normalized.Should().NotBeNull();
        normalized!.DateSource.Should().Be(DateSource.AutoFixed);
        normalized.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
    }

    [Fact]
    public void NormalizeDateSources_WhenTaskIsAutoFixed_ScheduledDateIsNeverNullAfterNormalization()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(702)
            .WithTitle("AutoFixed task without date")
            .WithRecurrence(RecurrenceType.EveryN, interval: 2, unit: RecurrenceUnit.Days)
            .WithScheduledDate(null, DateSource.AutoFixed)
            .WithStatus(TaskStatus.Planned)
            .Build();

        Context.Tasks.Add(task);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();

        // Act
        TaskDateNormalizer.NormalizeDateSources(Context, new TaskRecurrenceService());
        Context.SaveChanges();

        // Assert
        var normalized = Context.Tasks.Find(702);
        normalized.Should().NotBeNull();
        normalized!.DateSource.Should().Be(DateSource.AutoFixed);
        normalized.ScheduledDate.Should().NotBeNull();
    }
}
