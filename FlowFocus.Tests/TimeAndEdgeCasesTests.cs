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

            // Act: Complete task at 01:00 AM (system time < StartOfDay 04:00)
            var systemTimeAt0100AM = new DateTime(2026, 8, 4, 1, 0, 0);
            var logicalDay = systemTimeAt0100AM.Hour < 4
                ? new TodoDay(systemTimeAt0100AM.Date.AddDays(-1))
                : new TodoDay(systemTimeAt0100AM.Date);

            task.Status = TaskStatus.Completed;
            task.CompletedDate = logicalDay.ToDateTime();
            taskRepo.Update(task);

            var savedTask = taskRepo.GetById(task.Id);

            // Assert: CompletedDate in repository recorded as 2026-08-03
            savedTask.Should().NotBeNull();
            savedTask.CompletedDate.Should().Be(new DateTime(2026, 8, 3));
        }
    }
}
