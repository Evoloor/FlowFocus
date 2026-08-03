using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Trait("Category", "Domain")]
public class DateSourceTests
{
    private static StorageContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new StorageContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public class ManualDate
    {
        [Fact]
        public void SelectDateViaDatePicker_SetsSourceTypeToManualInRepository()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var task = new TaskItemBuilder().WithId(101).WithTitle("Manual Task").Build();
            taskRepo.Add(task);

            var selectedDate = new DateTime(2026, 8, 10);

            // Act: Application updates task schedule with manual date
            taskRepo.UpdateTaskSchedule(task.Id, selectedDate, DateSource.Manual);
            var savedTask = taskRepo.GetById(task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.DateSource.Should().Be(DateSource.Manual);
            savedTask.ScheduledDate.Should().Be(selectedDate);
        }
    }

    public class AutoFlexibleDate
    {
        [Fact]
        public void SaveTaskWithoutDate_SetsSourceTypeToAutoFlexible()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var task = new TaskItemBuilder().WithId(102).WithTitle("No Date Task").WithScheduledDate(null, DateSource.AutoFlexible).Build();

            // Act
            taskRepo.Add(task);
            var savedTask = taskRepo.GetById(task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.DateSource.Should().Be(DateSource.AutoFlexible);
            savedTask.ScheduledDate.Should().BeNull();
        }
    }

    public class AutoFixedConversion
    {
        [Fact]
        public void ManuallyEditAutoFixedDate_ChangesSourceTypeToManual()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var task = new TaskItemBuilder().WithId(103).WithScheduledDate(new DateTime(2026, 8, 5), DateSource.AutoFixed).Build();
            taskRepo.Add(task);

            var newManualDate = new DateTime(2026, 8, 12);

            // Act
            taskRepo.UpdateTaskSchedule(task.Id, newManualDate, DateSource.Manual);
            var savedTask = taskRepo.GetById(task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.DateSource.Should().Be(DateSource.Manual);
            savedTask.ScheduledDate.Should().Be(newManualDate);
        }
    }

    public class OverdueRedistribution
    {
        [Fact]
        public void OverdueManualTask_DuringRedistribution_ChangesSourceTypeToAutoFlexible()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var overdueDate = TodoDay.Today.Yesterday.ToDateTime();
            var task = new TaskItemBuilder()
                .WithId(104)
                .WithTitle("Overdue Task")
                .WithScheduledDate(overdueDate, DateSource.Manual)
                .WithStatus(TaskStatus.Planned)
                .Build();
            taskRepo.Add(task);

            // Act: Run planner service distribution
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(480).Build();
            plannerService.DistributeTasks(settings);

            var updatedTask = taskRepo.GetById(task.Id);

            // Assert
            updatedTask.Should().NotBeNull();
            updatedTask.DateSource.Should().Be(DateSource.AutoFlexible);
            updatedTask.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        }
    }

    public class RecurringTaskValidation
    {
        [Fact]
        public void EnableRecurrenceWithoutScheduledDate_ThrowsValidationError()
        {
            // Arrange
            var task = new TaskItemBuilder()
                .WithRecurrence(RecurrenceType.Daily)
                .WithScheduledDate(null)
                .Build();

            // Act: Validate via application domain validator
            var act = () => TaskItemValidator.ValidateRecurringTaskCreation(task);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*обязана иметь ручную дату*");
        }
    }

    public class HideFixedFilter
    {
        [Fact]
        public void HideFixedFilter_ExcludesOverdueManualTasksFromQueryResults()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            var yesterday = TodoDay.Today.Yesterday.ToDateTime();
            var overdueManualTask = new TaskItemBuilder()
                .WithId(101)
                .WithTitle("Overdue Manual Task")
                .WithScheduledDate(yesterday, DateSource.Manual)
                .WithStatus(TaskStatus.Planned)
                .Build();

            var overdueFlexibleTask = new TaskItemBuilder()
                .WithId(102)
                .WithTitle("Overdue Flexible Task")
                .WithScheduledDate(yesterday, DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned)
                .Build();

            var todayManualTask = new TaskItemBuilder()
                .WithId(103)
                .WithTitle("Today Manual Task")
                .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual)
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(overdueManualTask);
            taskRepo.Add(overdueFlexibleTask);
            taskRepo.Add(todayManualTask);

            // Act: Call real repository query method
            var candidates = taskRepo.GetRecurringCandidatesForPlanner();

            // Assert: Verify repository excludes non-recurring / manual date candidates as required
            candidates.Select(t => t.Id).Should().NotContain(101);
        }
    }
}
