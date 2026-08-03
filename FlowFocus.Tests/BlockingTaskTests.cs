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
    public void DistributeTasks_BlockedTaskDateIsNotEarlierThanBlockingTaskDate()
    {
        using var context = CreateInMemoryContext();
        var urgentPriority = context.Priorities.OrderBy(p => p.Order).First();

        var taskA = new TaskItem { Id = 201, Title = "Blocking Task A", PriorityId = urgentPriority.Id, DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 100 };
        var taskB = new TaskItem { Id = 202, Title = "Blocked Task B", PriorityId = urgentPriority.Id, DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 100 };
        context.Tasks.AddRange(taskA, taskB);

        var relation = new TaskRelation { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(relation);
        context.SaveChanges();

        var notificationService = new NotificationService();
        var taskRepo = new TaskRepository(context, notificationService);
        var plannerService = new PlannerService(taskRepo);

        // Daily limit of 100 minutes means taskA (100 min) fits today, taskB (100 min) moves to tomorrow!
        var settings = new UserSettings { DailyTimeLimit = 100, DailyComplexityLimit = 100, DailyTaskLimit = 10 };
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

    [Fact]
    public void NormalizeBlockerPriorities_ElevatesBlockerPriorityToBlockedTaskPriority()
    {
        using var context = CreateInMemoryContext();
        var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
        var highPriority = priorities[0];
        var lowPriority = priorities.Last();

        var taskA = new TaskItem { Id = 401, Title = "Low priority blocker A", PriorityId = lowPriority.Id, Status = TaskStatus.Planned };
        var taskB = new TaskItem { Id = 402, Title = "High priority blocked task B", PriorityId = highPriority.Id, Status = TaskStatus.Planned };
        context.Tasks.AddRange(taskA, taskB);

        var relation = new TaskRelation { Id = 4001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(relation);
        context.SaveChanges();

        var notificationService = new NotificationService();
        var taskRepo = new TaskRepository(context, notificationService);
        var plannerService = new PlannerService(taskRepo);

        plannerService.NormalizeBlockerPriorities();

        var updatedTaskA = taskRepo.GetById(taskA.Id);
        Assert.NotNull(updatedTaskA);
        Assert.Equal(highPriority.Id, updatedTaskA.PriorityId);
    }

    [Fact]
    public void DistributeTasks_KeepsBlockersAutoFlexibleWhenNoDeficit()
    {
        using var context = CreateInMemoryContext();
        var today = TodoDay.Today.ToDateTime();
        var dayAfterTomorrow = today.AddDays(2);

        var taskA = new TaskItem { Id = 501, Title = "Blocker A", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 100 };
        var taskB = new TaskItem { Id = 502, Title = "Blocked B", ScheduledDate = dayAfterTomorrow, DateSource = DateSource.Manual, Status = TaskStatus.Planned, EstimatedMinutes = 100 };
        context.Tasks.AddRange(taskA, taskB);

        var relation = new TaskRelation { Id = 5001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(relation);
        context.SaveChanges();

        var notificationService = new NotificationService();
        var taskRepo = new TaskRepository(context, notificationService);
        var plannerService = new PlannerService(taskRepo);

        var settings = new UserSettings { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };
        plannerService.DistributeTasks(settings);

        var updatedTaskA = taskRepo.GetById(taskA.Id);
        Assert.NotNull(updatedTaskA);
        Assert.Equal(DateSource.AutoFlexible, updatedTaskA.DateSource);
    }
}
