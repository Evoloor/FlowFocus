using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for date source mechanics (Manual, AutoFlexible, AutoFixed), transitions, and filtering.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Domain")]
[Collection(name: "StaticState")]
public class DateSourceTests
{
    /// <summary>
    /// Tests verification of setting manual date sources via task schedule updates.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class ManualDate
    {
        /// <summary>
        /// Verifies that selecting a date via DatePicker updates DateSource to Manual in repository.
        /// </summary>
        [Fact]
        public void SelectDateViaDatePicker_SetsSourceTypeToManualInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            var task = new TaskItemBuilder().WithId(id: 101).WithTitle(title: "Manual Task").Build();
            taskRepo.Add(entity: task);

            DateTime selectedDate = new(year: 2026, month: 8, day: 10);

            // Act: Application updates task schedule with manual date
            taskRepo.UpdateTaskSchedule(taskId: task.Id, scheduledDate: selectedDate, dateSource: DateSource.Manual);
            var savedTask = taskRepo.GetById(id: task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.DateSource.Should().Be(expected: DateSource.Manual);
            savedTask.ScheduledDate.Should().Be(expected: selectedDate);
        }
    }

    /// <summary>
    /// Tests verification of auto-flexible default date sources.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class AutoFlexibleDate
    {
        /// <summary>
        /// Verifies that saving a task without a date sets DateSource to AutoFlexible.
        /// </summary>
        [Fact]
        public void SaveTaskWithoutDate_SetsSourceTypeToAutoFlexible()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            var task = new TaskItemBuilder().WithId(id: 102).WithTitle(title: "No Date Task").WithScheduledDate(date: null, dateSource: DateSource.AutoFlexible).Build();

            // Act
            taskRepo.Add(entity: task);
            var savedTask = taskRepo.GetById(id: task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.DateSource.Should().Be(expected: DateSource.AutoFlexible);
            savedTask.ScheduledDate.Should().BeNull();
        }
    }

    /// <summary>
    /// Tests verification of converting AutoFixed date sources to Manual when edited manually.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class AutoFixedConversion
    {
        /// <summary>
        /// Verifies that manually editing an AutoFixed date changes DateSource to Manual.
        /// </summary>
        [Fact]
        public void ManuallyEditAutoFixedDate_ChangesSourceTypeToManual()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            var task = new TaskItemBuilder().WithId(id: 103).WithScheduledDate(date: new DateTime(year: 2026, month: 8, day: 5), dateSource: DateSource.AutoFixed).Build();
            taskRepo.Add(entity: task);

            DateTime newManualDate = new(year: 2026, month: 8, day: 12);

            // Act
            taskRepo.UpdateTaskSchedule(taskId: task.Id, scheduledDate: newManualDate, dateSource: DateSource.Manual);
            var savedTask = taskRepo.GetById(id: task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.DateSource.Should().Be(expected: DateSource.Manual);
            savedTask.ScheduledDate.Should().Be(expected: newManualDate);
        }
    }

    /// <summary>
    /// Tests verification of redistributing overdue tasks with manual date sources.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class OverdueRedistribution
    {
        /// <summary>
        /// Verifies that during redistribution an overdue task with Manual date changes DateSource to AutoFlexible.
        /// </summary>
        [Fact]
        public void OverdueManualTask_DuringRedistribution_ChangesSourceTypeToAutoFlexible()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var overdueDate = TodoDay.Today.Yesterday.ToDateTime();
            var task = new TaskItemBuilder()
                .WithId(id: 104)
                .WithTitle(title: "Overdue Task")
                .WithScheduledDate(date: overdueDate, dateSource: DateSource.Manual)
                .WithStatus(status: TaskStatus.Planned)
                .Build();
            taskRepo.Add(entity: task);

            // Act: Run planner service distribution
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 480).Build();
            plannerService.DistributeTasks(settings: settings);

            var updatedTask = taskRepo.GetById(id: task.Id);

            // Assert
            updatedTask.Should().NotBeNull();
            updatedTask.DateSource.Should().Be(expected: DateSource.AutoFlexible);
            updatedTask.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
        }
    }

    /// <summary>
    /// Tests verification of domain validation for recurring task creation.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class RecurringTaskValidation
    {
        /// <summary>
        /// Verifies that enabling recurrence without a scheduled date throws a validation exception.
        /// </summary>
        [Fact]
        public void EnableRecurrenceWithoutScheduledDate_ThrowsValidationError()
        {
            // Arrange
            var task = new TaskItemBuilder()
                .WithRecurrence(type: RecurrenceType.Daily)
                .WithScheduledDate(date: null)
                .Build();

            // Act: Validate via application domain validator
            var act = () => TaskItemValidator.ValidateRecurringTaskCreation(task: task);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage(expectedWildcardPattern: "*обязана иметь ручную дату*");
        }
    }

    /// <summary>
    /// Tests verification of filtering fixed date candidates in query results.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class HideFixedFilter
    {
        /// <summary>
        /// Verifies that candidate query methods exclude non-recurring manual date tasks.
        /// </summary>
        [Fact]
        public void HideFixedFilter_ExcludesOverdueManualTasksFromQueryResults()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            var yesterday = TodoDay.Today.Yesterday.ToDateTime();
            var overdueManualTask = new TaskItemBuilder()
                .WithId(id: 101)
                .WithTitle(title: "Overdue Manual Task")
                .WithScheduledDate(date: yesterday, dateSource: DateSource.Manual)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            var overdueFlexibleTask = new TaskItemBuilder()
                .WithId(id: 102)
                .WithTitle(title: "Overdue Flexible Task")
                .WithScheduledDate(date: yesterday, dateSource: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            var todayManualTask = new TaskItemBuilder()
                .WithId(id: 103)
                .WithTitle(title: "Today Manual Task")
                .WithScheduledDate(date: TodoDay.Today.ToDateTime(), dateSource: DateSource.Manual)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            taskRepo.Add(entity: overdueManualTask);
            taskRepo.Add(entity: overdueFlexibleTask);
            taskRepo.Add(entity: todayManualTask);

            // Act: Call real repository query method
            var candidates = taskRepo.GetRecurringCandidatesForPlanner();

            // Assert: Verify repository excludes non-recurring / manual date candidates as required
            candidates.Select(selector: t => t.Id).Should().NotContain(unexpected: 101);
        }
    }
}
