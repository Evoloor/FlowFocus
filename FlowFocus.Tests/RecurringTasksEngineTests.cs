using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Data.Services;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for recurring task generation algorithms, daily/monthly/yearly rules, subtask cascading, and idempotency.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Recurrence")]
[Collection(name: "StaticState")]
public class RecurringTasksEngineTests
{
    /// <summary>
    /// Tests verification of daily recurrence copy generation.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Recurrence")]
    public class DailyRecurrence
    {
        /// <summary>
        /// Verifies that completing a daily task creates a new copy for tomorrow in repository.
        /// </summary>
        [Fact]
        public void CompleteDailyTask_CreatesNewCopyForTomorrowInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            var today = TodoDay.Today.ToDateTime();
            var task = new TaskItemBuilder()
                .WithId(id: 100)
                .WithTitle(title: "Daily Task")
                .WithScheduledDate(date: today, dateSource: DateSource.AutoFixed)
                .WithRecurrence(type: RecurrenceType.Daily)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            taskRepo.Add(entity: task);

            // Act: Call real application service method
            taskRepo.CompleteTask(taskId: task.Id);

            var allTasks = taskRepo.GetAll();
            var completedTask = taskRepo.GetById(id: task.Id);
            var newCopy = allTasks.FirstOrDefault(predicate: t => t.RecurrenceSourceId == task.Id);

            // Assert: Verify persistent repository state
            completedTask!.Status.Should().Be(expected: TaskStatus.Completed);
            newCopy.Should().NotBeNull();
            newCopy.Title.Should().Be(expected: "Daily Task");
            newCopy.ScheduledDate.Should().Be(expected: today.AddDays(value: 1));
            newCopy.DateSource.Should().Be(expected: DateSource.AutoFixed);
            newCopy.Status.Should().Be(expected: TaskStatus.Planned);
        }
    }

    /// <summary>
    /// Tests verification of overdue completion next date calculation.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Recurrence")]
    public class OverdueCompletion
    {
        /// <summary>
        /// Verifies that completing an overdue task calculates next recurrence date from actual completion date.
        /// </summary>
        [Fact]
        public void CompleteOverdueTask_CalculatesNextDateFromActualCompletionDate()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            var overdueDate = TodoDay.Today.Yesterday.AddDays(days: -2).ToDateTime(); // 3 days ago
            var task = new TaskItemBuilder()
                .WithId(id: 200)
                .WithTitle(title: "Overdue Daily Task")
                .WithScheduledDate(date: overdueDate, dateSource: DateSource.AutoFixed)
                .WithRecurrence(type: RecurrenceType.Daily)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            taskRepo.Add(entity: task);

            // Act: Call application service method to complete today
            taskRepo.CompleteTask(taskId: task.Id);

            var allTasks = taskRepo.GetAll();
            var newCopy = allTasks.FirstOrDefault(predicate: t => t.RecurrenceSourceId == task.Id);

            // Assert: Scheduled date calculated from actual completion day (Today + 1 day)
            newCopy.Should().NotBeNull();
            newCopy.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }
    }

    /// <summary>
    /// Tests verification of monthly and yearly recurrence calculations.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Recurrence")]
    public class MonthlyYearly
    {
        /// <summary>
        /// Verifies that completing a monthly task calculates next date in following month.
        /// </summary>
        [Fact]
        public void CompleteMonthlyTask_CreatesCopyNextMonth()
        {
            // Arrange
            TaskRecurrenceService recurrenceService = new();
            DateTime aug3 = new(year: 2026, month: 8, day: 3);

            var task = new TaskItemBuilder()
                .WithId(id: 300)
                .WithRecurrence(type: RecurrenceType.Monthly)
                .WithCompletedDate(date: aug3)
                .Build();

            // Act: Call real recurrence service
            var nextDate = recurrenceService.CalculateNextRecurrenceDate(task: task);

            // Assert
            nextDate.Should().Be(expected: new(year: 2026, month: 9, day: 3));
        }

        /// <summary>
        /// Verifies that completing a Jan 31 monthly task calculates Feb 28 safely without invalid date errors.
        /// </summary>
        [Fact]
        public void CompleteJan31MonthlyTask_CalculatesFeb28WithoutInvalidDateError()
        {
            // Arrange
            TaskRecurrenceService recurrenceService = new();
            DateTime jan31 = new(year: 2026, month: 1, day: 31);

            var task = new TaskItemBuilder()
                .WithId(id: 301)
                .WithRecurrence(type: RecurrenceType.Monthly)
                .WithScheduledDate(date: jan31)
                .WithCompletedDate(date: jan31)
                .Build();

            // Act: Call real recurrence service
            var nextDate = recurrenceService.CalculateNextRecurrenceDate(task: task);

            // Assert: Handles month end boundary safely
            nextDate.Should().Be(expected: new(year: 2026, month: 2, day: 28));
        }

        /// <summary>
        /// Verifies that completing a yearly task calculates next date in following year.
        /// </summary>
        [Fact]
        public void CompleteYearlyTask_CreatesCopyNextYear()
        {
            // Arrange
            TaskRecurrenceService recurrenceService = new();
            DateTime aug3 = new(year: 2026, month: 8, day: 3);

            var task = new TaskItemBuilder()
                .WithId(id: 302)
                .WithRecurrence(type: RecurrenceType.Yearly)
                .WithScheduledDate(date: aug3)
                .WithCompletedDate(date: aug3)
                .Build();

            // Act: Call real recurrence service
            var nextDate = recurrenceService.CalculateNextRecurrenceDate(task: task);

            // Assert: Date set to same day next year (2027-08-03)
            nextDate.Should().Be(expected: new(year: 2027, month: 8, day: 3));
        }
    }

    /// <summary>
    /// Tests verification of subtask cascading on recurring task completion.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Recurrence")]
    public class CascadeSubtaskCopy
    {
        /// <summary>
        /// Verifies that completing a recurring task cascades subtasks to the newly created copy in repository.
        /// </summary>
        [Fact]
        public void CompleteRecurringTask_CascadesSubtasksToNewCopyInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            TaskItem sub1 = new() { Title = "Subtask 1" };
            TaskItem sub2 = new() { Title = "Subtask 2" };

            TaskItem parent = new()
            {
                Title = "Parent Recurring Task",
                IsRecurring = true,
                RecurrenceType = RecurrenceType.Daily,
                ScheduledDate = TodoDay.Today.ToDateTime(),
                Status = TaskStatus.Planned,
                Subtasks = [sub1, sub2]
            };

            taskRepo.Add(entity: parent);
            context.ChangeTracker.Clear();

            // Act: Call application service method
            taskRepo.CompleteTask(taskId: parent.Id);

            var allTasks = taskRepo.GetAll();
            var newCopy = allTasks.FirstOrDefault(predicate: t => t.RecurrenceSourceId == parent.Id);

            // Assert: Subtasks copied and attached to new parent copy in DB
            newCopy.Should().NotBeNull();
            newCopy.Subtasks.Should().HaveCount(expected: 2);
            newCopy.Subtasks.Select(selector: s => s.Title).Should().ContainInConsecutiveOrder(expected: ["Subtask 1", "Subtask 2"]);
        }
    }

    /// <summary>
    /// Tests verification of rapid click idempotency during task completion.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Recurrence")]
    public class RapidClicksIdempotency
    {
        /// <summary>
        /// Verifies that rapid double-clicks on completion generates only a single copy in repository.
        /// </summary>
        [Fact]
        public void RapidClicksOnCompletion_GeneratesOnlySingleCopy()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            var task = new TaskItemBuilder()
                .WithId(id: 500)
                .WithTitle(title: "Rapid Click Task")
                .WithRecurrence(type: RecurrenceType.Daily)
                .WithScheduledDate(date: TodoDay.Today.ToDateTime())
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            taskRepo.Add(entity: task);

            // Act: Rapid double-click on complete task
            taskRepo.CompleteTask(taskId: task.Id);
            taskRepo.CompleteTask(taskId: task.Id);

            var copies = taskRepo.GetAll().Where(predicate: t => t.RecurrenceSourceId == task.Id).ToList();

            // Assert: Only single recurring copy generated in repository
            copies.Should().HaveCount(expected: 1);
        }
    }
}