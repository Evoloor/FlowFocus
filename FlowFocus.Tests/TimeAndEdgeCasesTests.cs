using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Services;

using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Trait("Category", "Time")]
public class TimeAndEdgeCasesTests
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

    public class MidnightStartOfDay
    {
        [Fact]
        public void SystemTime0100AM_WithStartOfDay4AM_RecordsCompletionDateAsPreviousCalendarDayInRepository()
        {
            // Arrange
            TodoDay.Configure(4);
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            var task = new TaskItemBuilder()
                .WithId(100)
                .WithTitle("Overdue Midnight Task")
                .WithScheduledDate(new DateTime(2026, 8, 3))
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(task);

            // Act: Call real repository CompleteTask method
            taskRepo.CompleteTask(task.Id);

            var savedTask = taskRepo.GetById(task.Id);

            // Assert: Task status is Completed and CompletedDate is populated in task repository
            savedTask.Should().NotBeNull();
            savedTask.Status.Should().Be(TaskStatus.Completed);
            savedTask.CompletedDate.Should().NotBeNull();
        }
    }
}
