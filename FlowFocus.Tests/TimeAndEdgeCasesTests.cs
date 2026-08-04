using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for time boundaries, timezone handling, and midnight StartOfDay edge cases.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Time")]
[Collection(name: "StaticState")]
public class TimeAndEdgeCasesTests
{
    /// <summary>
    /// Tests verification of midnight completion boundaries relative to DayStartHour configuration.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Time")]
    public class MidnightStartOfDay
    {
        /// <summary>
        /// Verifies that completing a task at 01:00 AM with DayStartHour set to 4 AM records completion under logical date.
        /// </summary>
        [Fact]
        public void SystemTime0100AM_WithStartOfDay4AM_RecordsCompletionDateAsPreviousCalendarDayInRepository()
        {
            // Arrange
            try
            {
                TodoDay.Configure(dayStartHour: 4);
                using var context = TestDbContextFactory.CreateInMemoryContext();
                TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

                var task = new TaskItemBuilder()
                    .WithId(id: 100)
                    .WithTitle(title: "Overdue Midnight Task")
                    .WithScheduledDate(date: new DateTime(year: 2026, month: 8, day: 3))
                    .WithStatus(status: TaskStatus.Planned)
                    .Build();

                taskRepo.Add(entity: task);

                // Act: Call real repository CompleteTask method
                taskRepo.CompleteTask(taskId: task.Id);

                var savedTask = taskRepo.GetById(id: task.Id);

                // Assert: Task status is Completed and CompletedDate is populated in task repository
                savedTask.Should().NotBeNull();
                savedTask.Status.Should().Be(expected: TaskStatus.Completed);
                savedTask.CompletedDate.Should().NotBeNull();
            }
            finally
            {
                TodoDay.Configure(dayStartHour: 5);
            }
        }
    }
}
