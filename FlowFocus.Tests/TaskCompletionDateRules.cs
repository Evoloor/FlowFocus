using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class TaskCompletionDateRules
{
    [Fact]
    public void CompletingOverdueTask_SetsCompletionDateToOriginalScheduledDate()
    {
        // Arrange: Просроченные задачи завершаются тем числом, на которое были назначены
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            
        var pastDate = TodoDay.Today.ToDateTime().AddDays(-5);
        var overdueTask = new TaskItemBuilder()
            .WithId(101).WithScheduledDate(pastDate, dateSource: DateSource.Manual).WithStatus(TaskStatus.Planned).Build();
            
        taskRepo.Add(overdueTask);

        // Act
        taskRepo.CompleteTask(overdueTask.Id);

        // Assert
        var saved = taskRepo.GetById(101);
        saved!.CompletedDate.Should().Be(pastDate, "Просроченная задача должна 'остаться' в дне своего назначения");
    }

    [Fact]
    public void CompletingFutureTask_SetsCompletionDateToToday()
    {
        // Arrange: Предстоящие дела при досрочном завершении завершаются сегодняшним днем
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            
        var futureDate = TodoDay.Today.ToDateTime().AddDays(5);
        var futureTask = new TaskItemBuilder()
            .WithId(102).WithScheduledDate(futureDate, dateSource: DateSource.Manual).WithStatus(TaskStatus.Planned).Build();
            
        taskRepo.Add(futureTask);

        // Act
        taskRepo.CompleteTask(futureTask.Id);

        // Assert
        var saved = taskRepo.GetById(102);
        saved!.CompletedDate.Should().Be(TodoDay.Today.ToDateTime(), "Будущая задача при выполнении переносится в сегодняшний список завершенных");
    }
}