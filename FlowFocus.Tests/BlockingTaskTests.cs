using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class BlockingTaskTests
{
    private StorageContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new StorageContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void NormalizeBlockingTaskPriorities_UpgradesBlockingTaskPriorityToMatchBlockedTask()
    {
        using var context = CreateInMemoryContext();
        var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
        var urgentPriority = priorities.First(); // lowest Order = highest priority
        var lowPriority = priorities.Last();   // highest Order = lowest priority

        var taskA = new TaskItem { Id = 101, Title = "Blocking Task A", PriorityId = lowPriority.Id, Status = TaskStatus.Planned };
        var taskB = new TaskItem { Id = 102, Title = "Blocked Task B", PriorityId = urgentPriority.Id, Status = TaskStatus.Planned };
        context.Tasks.AddRange(taskA, taskB);

        var relation = new TaskRelation { Id = 1001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(relation);
        context.SaveChanges();

        var notificationService = new NotificationService();
        var taskRepo = new TaskRepository(context, notificationService);

        taskRepo.NormalizeBlockingTaskPriorities();

        var updatedTaskA = taskRepo.GetById(taskA.Id);
        Assert.NotNull(updatedTaskA);
        Assert.Equal(urgentPriority.Id, updatedTaskA.PriorityId);
    }

    [Fact]
    public void DistributeTasks_BlockedTaskDateIsNotEarlierThanBlockingTaskDate()
    {
        using var context = CreateInMemoryContext();
        var urgentPriority = context.Priorities.OrderBy(p => p.Order).First();

        var taskA = new TaskItem { Id = 201, Title = "Blocking Task A", PriorityId = urgentPriority.Id, DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 300 };
        var taskB = new TaskItem { Id = 202, Title = "Blocked Task B", PriorityId = urgentPriority.Id, DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 300 };
        context.Tasks.AddRange(taskA, taskB);

        var relation = new TaskRelation { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(relation);
        context.SaveChanges();

        var notificationService = new NotificationService();
        var taskRepo = new TaskRepository(context, notificationService);
        var plannerService = new PlannerService(taskRepo);

        // Daily limit of 300 minutes means taskA and taskB cannot fit on the same day!
        var settings = new UserSettings { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };
        plannerService.DistributeTasks(settings);

        var updatedTaskA = taskRepo.GetById(taskA.Id);
        var updatedTaskB = taskRepo.GetById(taskB.Id);

        Assert.NotNull(updatedTaskA?.ScheduledDate);
        Assert.NotNull(updatedTaskB?.ScheduledDate);
        Assert.True(updatedTaskB.ScheduledDate >= updatedTaskA.ScheduledDate, "Blocked task scheduled date must be >= blocking task scheduled date");
    }

    [Fact]
    public void ApproachingDeadline_ConvertsBlockersToAutoFixedWhenBufferExhausted()
    {
        using var context = CreateInMemoryContext();
        var today = TodoDay.Today.ToDateTime();
        var tomorrow = today.AddDays(1);

        var taskA = new TaskItem { Id = 301, Title = "Blocking Task A", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 300 };
        var taskB = new TaskItem { Id = 302, Title = "Blocked Task B with Manual Date", ScheduledDate = tomorrow, DateSource = DateSource.Manual, Status = TaskStatus.Planned, EstimatedMinutes = 300 };
        context.Tasks.AddRange(taskA, taskB);

        var relation = new TaskRelation { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(relation);
        context.SaveChanges();

        var notificationService = new NotificationService();
        var taskRepo = new TaskRepository(context, notificationService);
        var plannerService = new PlannerService(taskRepo);

        // Daily limit 300 mins. Total time A+B = 600 mins -> required days = 2.
        // Start date = tomorrow - 1 day = Today. Since start date <= Today, deadline is approaching!
        var settings = new UserSettings { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };
        plannerService.DistributeTasks(settings);

        var updatedTaskA = taskRepo.GetById(taskA.Id);
        Assert.NotNull(updatedTaskA);
        Assert.Equal(DateSource.AutoFixed, updatedTaskA.DateSource);
        Assert.Equal(tomorrow, updatedTaskA.ScheduledDate);
    }
}
