using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using JetBrains.Annotations;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for blocking tasks, blocker date distribution, deadline buffer escalation, and completion unblocking.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Relations")]
[Collection(name: "StaticState")]
public class BlockingTaskTests
{
    /// <summary>
    /// Verifies that blocked task scheduled date is not earlier than blocking task scheduled date.
    /// </summary>
    [Fact]
    public void DistributeTasks_BlockedTaskDateIsNotEarlierThanBlockingTaskDate()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var urgentPriority = context.Priorities.OrderBy(keySelector: p => p.Order).First();

        TaskItem taskA = new()
        {
            Id = 201, Title = "Blocking Task A", PriorityId = urgentPriority.Id, DateSource = DateSource.AutoFlexible,
            Status = TaskStatus.Planned, EstimatedMinutes = 300
        };
        TaskItem taskB = new()
        {
            Id = 202, Title = "Blocked Task B", PriorityId = urgentPriority.Id, DateSource = DateSource.AutoFlexible,
            Status = TaskStatus.Planned, EstimatedMinutes = 300
        };
        context.Tasks.AddRange(entities: [taskA, taskB]);

        TaskRelation relation = new() { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(entity: relation);
        context.SaveChanges();

        NotificationService notificationService = new();
        TaskRepository taskRepo = new(context: context, notificationService: notificationService);
        PlannerService plannerService = new(taskRepository: taskRepo);

        // Daily limit of 300 minutes means taskA and taskB cannot fit on the same day
        UserSettings settings = new() { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };

        // Act
        plannerService.DistributeTasks(settings: settings);

        var updatedTaskA = taskRepo.GetById(id: taskA.Id);
        var updatedTaskB = taskRepo.GetById(id: taskB.Id);

        // Assert
        Assert.NotNull(value: updatedTaskA?.ScheduledDate);
        Assert.NotNull(value: updatedTaskB?.ScheduledDate);
        Assert.True(condition: updatedTaskB.ScheduledDate >= updatedTaskA.ScheduledDate,
            userMessage: "Blocked task scheduled date must be >= blocking task scheduled date");
    }

    /// <summary>
    /// Verifies that approaching deadlines convert blockers to AutoFixed when buffer is exhausted.
    /// </summary>
    [Fact]
    public void ApproachingDeadline_ConvertsBlockersToAutoFixedWhenBufferExhausted()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var today = TodoDay.Today.ToDateTime();
        var tomorrow = today.AddDays(value: 1);

        TaskItem taskA = new()
        {
            Id = 301, Title = "Blocking Task A", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned,
            EstimatedMinutes = 300
        };
        TaskItem taskB = new()
        {
            Id = 302, Title = "Blocked Task B with Manual Date", ScheduledDate = tomorrow,
            DateSource = DateSource.Manual, Status = TaskStatus.Planned, EstimatedMinutes = 300
        };
        context.Tasks.AddRange(entities: [taskA, taskB]);

        TaskRelation relation = new() { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(entity: relation);
        context.SaveChanges();

        NotificationService notificationService = new();
        TaskRepository taskRepo = new(context: context, notificationService: notificationService);
        PlannerService plannerService = new(taskRepository: taskRepo);

        // Daily limit 300 mins. Total time A+B = 600 mins -> required days = 2.
        UserSettings settings = new() { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };

        // Act
        plannerService.DistributeTasks(settings: settings);

        var updatedTaskA = taskRepo.GetById(id: taskA.Id);

        // Assert
        Assert.NotNull(@object: updatedTaskA);
        Assert.Equal(expected: DateSource.AutoFixed, actual: updatedTaskA.DateSource);
        Assert.Equal(expected: tomorrow, actual: updatedTaskA.ScheduledDate);
    }

    /// <summary>
    /// Verifies that unconfigured tasks transition to Blocked status when saved with active blockers.
    /// </summary>
    [Fact]
    public void NotConfiguredTask_WhenSavedWithActiveBlocker_TransitionsToBlockedStatus()
    {
        // Arrange: Unconfigured task retains status when linked, but transitions to Blocked on saving
        using var context = TestDbContextFactory.CreateInMemoryContext();
        TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

        var blocker = new TaskItemBuilder().WithId(id: 201).WithStatus(status: TaskStatus.Planned).Build();
        var notConfiguredTask = new TaskItemBuilder().WithId(id: 202).WithStatus(status: TaskStatus.NotConfigured).Build();

        context.Tasks.AddRange(entities: [blocker, notConfiguredTask]);
        context.TaskRelations.Add(entity: new() { SourceTaskId = blocker.Id, TargetTaskId = notConfiguredTask.Id, Type = RelationType.Blocks });
        context.SaveChanges();

        // Act 1: Verify relation addition did not automatically alter status
        var taskBeforeEdit = taskRepo.GetById(id: notConfiguredTask.Id);
        taskBeforeEdit!.Status.Should().Be(expected: TaskStatus.NotConfigured);

        // Act 2: User configures task and saves
        taskBeforeEdit.Title = "Настроенное название";
        taskBeforeEdit.Status = TaskStatus.Planned;

        taskRepo.Update(entity: taskBeforeEdit);

        // Assert: Repository/Service forces status to "Blocked"
        var savedTask = taskRepo.GetById(id: notConfiguredTask.Id);
        savedTask!.Status.Should().Be(expected: TaskStatus.Blocked);
    }

    /// <summary>
    /// Verifies that priority escalation of a blocked task forces cascade priority increases on blockers.
    /// </summary>
    [Fact]
    public void AutoEscalation_OfBlockedTask_ForcesCascadePriorityIncreaseOnBlockers()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryContext();
        TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
        PlannerService plannerService = new(taskRepository: taskRepo);

        var priorities = context.Priorities.OrderBy(keySelector: p => p.Order).ToList();
        var critical = priorities[index: 0];
        var low = priorities[index: 3];

        var blocker = new TaskItemBuilder().WithId(id: 501).WithPriorityId(priorityId: low.Id).WithStatus(status: TaskStatus.Planned).Build();
        var blocked = new TaskItemBuilder().WithId(id: 502).WithPriorityId(priorityId: low.Id).WithStatus(status: TaskStatus.Planned).Build();
        blocked.PriorityEscalations.Add(item: new() { TargetPriorityId = critical.Id, EscalationDate = TodoDay.Today.ToDateTime() });

        context.Tasks.AddRange(entities: [blocker, blocked]);
        context.TaskRelations.Add(entity: new() { SourceTaskId = blocker.Id, TargetTaskId = blocked.Id, Type = RelationType.Blocks });
        context.SaveChanges();

        // Act
        plannerService.ActualizePriorities();
        taskRepo.SaveChanges();

        // Assert
        var updatedBlocker = taskRepo.GetById(id: blocker.Id);
        var updatedBlocked = taskRepo.GetById(id: blocked.Id);

        updatedBlocked!.PriorityId.Should().Be(expected: critical.Id);
        updatedBlocker!.PriorityId.Should().Be(expected: critical.Id);
    }

    /// <summary>
    /// Tests verification of blocker distribution scheduling order.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Relations")]
    public class BlockerDistributionOrder
    {
        /// <summary>
        /// Verifies that DistributeTasks always schedules blockers before or on the same day as blocked tasks.
        /// </summary>
        [Fact]
        public void DistributeTasks_AlwaysSchedulesBlockerBeforeOrOnSameDayAsBlockedTask()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var taskABlocker = new TaskItemBuilder()
                .WithId(id: 601)
                .WithTitle(title: "Blocker A")
                .WithInterest(interest: 1)
                .WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            var taskBBlocked = new TaskItemBuilder()
                .WithId(id: 602)
                .WithTitle(title: "Blocked B")
                .WithInterest(interest: 10)
                .WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            context.Tasks.AddRange(entities: [taskABlocker, taskBBlocked]);
            context.TaskRelations.Add(entity: new() { SourceTaskId = taskABlocker.Id, TargetTaskId = taskBBlocked.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            taskABlocker.EstimatedMinutes = 45;
            taskBBlocked.EstimatedMinutes = 45;

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 60).Build();

            // Act
            plannerService.DistributeTasks(settings: settings);

            // Assert
            var updatedA = taskRepo.GetById(id: taskABlocker.Id);
            var updatedB = taskRepo.GetById(id: taskBBlocked.Id);

            updatedA.Should().NotBeNull();
            updatedB.Should().NotBeNull();
            updatedB.ScheduledDate.Should().BeOnOrAfter(expected: updatedA.ScheduledDate!.Value);
        }
    }

    /// <summary>
    /// Tests verification of incoming blocker relation cleanup on blocked task completion.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Relations")]
    public class BlockedTaskCompletion
    {
        /// <summary>
        /// Verifies that completing a blocked task removes incoming blocker relations.
        /// </summary>
        [Fact]
        public void CompleteOrMarkIrrelevantBlockedTask_RemovesIncomingBlockerRelations()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            TaskItem taskA1 = new() { Id = 701, Title = "Blocker A1", Status = TaskStatus.Planned };
            TaskItem taskA2 = new() { Id = 702, Title = "Blocker A2", Status = TaskStatus.Planned };
            TaskItem taskB = new() { Id = 703, Title = "Blocked Task B", Status = TaskStatus.Planned };

            context.Tasks.AddRange(entities: [taskA1, taskA2, taskB]);
            context.TaskRelations.Add(entity: new() { Id = 7001, SourceTaskId = taskA1.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.TaskRelations.Add(entity: new() { Id = 7002, SourceTaskId = taskA2.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            // Act
            taskRepo.CompleteTask(taskId: taskB.Id);

            // Assert
            var savedB = taskRepo.GetById(id: taskB.Id);
            var remainingRelationsToB = context.TaskRelations
                .Where(predicate: r => r.TargetTaskId == taskB.Id && r.Type == RelationType.Blocks)
                .ToList();

            savedB.Should().NotBeNull();
            savedB.Status.Should().Be(expected: TaskStatus.Completed);
            remainingRelationsToB.Should().BeEmpty();
        }
    }
}