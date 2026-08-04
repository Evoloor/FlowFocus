using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
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
/// Unit tests for task relations, blocker cascades, unblocking mechanics, and graph persistence.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Relations")]
[Collection(name: "StaticState")]
public class RelationsEngineTests
{
    /// <summary>
    /// Tests verification of blocker priority rules and normalization.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Relations")]
    public class BlockerPriority
    {
    }

    /// <summary>
    /// Tests verification of deadline ordering constraints between blockers and target tasks.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Relations")]
    public class CascadeDeadline
    {
        /// <summary>
        /// Verifies that setting a blocker deadline later than the blocked task deadline throws a validation exception.
        /// </summary>
        [Fact]
        public void BlockerDeadlineLaterThanBlockedTask_ThrowsValidationError()
        {
            // Arrange
            var blocker = new TaskItemBuilder().WithId(id: 1)
                .WithScheduledDate(date: new DateTime(year: 2026, month: 8, day: 15)).Build();
            var blocked = new TaskItemBuilder().WithId(id: 2)
                .WithScheduledDate(date: new DateTime(year: 2026, month: 8, day: 10)).Build();

            // Act: Call real domain validator
            var act = () =>
                TaskRelationValidator.ValidateNewRelation(sourceTask: blocker, targetTask: blocked,
                    type: RelationType.Blocks);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage(expectedWildcardPattern: "*не может быть позже дедлайна*");
        }
    }

    /// <summary>
    /// Tests verification of unblocking mechanics upon task completion.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Relations")]
    public class UnblockingOnCompletion
    {
        /// <summary>
        /// Verifies that completing the sole blocker removes blocked status from target task.
        /// </summary>
        [Fact]
        public void CompleteSoleBlocker_RemovesBlockedStatusFromTargetTask()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskItem taskA = new() { Id = 101, Title = "Blocker A", Status = TaskStatus.Planned };
            TaskItem taskB = new() { Id = 102, Title = "Blocked B", Status = TaskStatus.Planned };
            context.Tasks.AddRange(entities: [taskA, taskB]);

            context.TaskRelations.Add(entity: new()
                { Id = 1001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());

            // Act: Call real application service method
            taskRepo.CompleteTask(taskId: taskA.Id);

            // Assert: Inspect target task state in repository
            var updatedB = taskRepo.GetById(id: taskB.Id);
            updatedB.Should().NotBeNull();
            updatedB.IsBlocked.Should().BeFalse();
        }

        /// <summary>
        /// Verifies that completing one of multiple blockers leaves target task blocked by remaining blockers.
        /// </summary>
        [Fact]
        public void CompleteOneOfMultipleBlockers_TaskRemainsBlocked()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskItem taskA = new() { Id = 201, Title = "Blocker A", Status = TaskStatus.Planned };
            TaskItem taskC = new() { Id = 203, Title = "Blocker C", Status = TaskStatus.Planned };
            TaskItem taskB = new() { Id = 202, Title = "Blocked B", Status = TaskStatus.Planned };
            context.Tasks.AddRange(entities: [taskA, taskC, taskB]);

            context.TaskRelations.Add(entity: new()
                { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.TaskRelations.Add(entity: new()
                { Id = 2002, SourceTaskId = taskC.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());

            // Act: Call real application service method
            taskRepo.CompleteTask(taskId: taskA.Id);

            // Assert: Task B remains blocked by task C
            var updatedB = taskRepo.GetById(id: taskB.Id);
            updatedB.Should().NotBeNull();
            updatedB.IsBlocked.Should().BeTrue();
        }
    }

    /// <summary>
    /// Tests verification of bidirectional relation navigation properties.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Relations")]
    public class BidirectionalVisibility
    {
        /// <summary>
        /// Verifies that a single relation record in DB exposes bidirectional navigation properties in repository queries.
        /// </summary>
        [Fact]
        public void SingleRelationRecordInDb_ExposesBidirectionalNavigation()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskItem taskA = new() { Id = 301, Title = "Task A", Status = TaskStatus.Planned };
            TaskItem taskB = new() { Id = 302, Title = "Task B", Status = TaskStatus.Planned };
            context.Tasks.AddRange(entities: [taskA, taskB]);

            context.TaskRelations.Add(entity: new()
                { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());

            // Act: Query repository
            var fetchedA = taskRepo.GetById(id: taskA.Id);
            var fetchedB = taskRepo.GetById(id: taskB.Id);

            // Assert: Verify bidirectional state in application repository
            fetchedA!.Relations.Should()
                .ContainSingle(predicate: r => r.TargetTaskId == taskB.Id && r.Type == RelationType.Blocks);
            fetchedB!.InverseRelations.Should()
                .ContainSingle(predicate: r => r.SourceTaskId == taskA.Id && r.Type == RelationType.Blocks);
        }
    }

    /// <summary>
    /// Tests verification of blocker auto-fixed date calculation math.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Relations")]
    public class BlockerAutoFixedDateMath
    {
        /// <summary>
        /// Verifies that when chain hours exceed daily limits, blockers are assigned AutoFixed dates in advance before target deadline.
        /// </summary>
        [Fact]
        public void BlockerAutoFixedDateMath_CalculatesChainHoursDividedByDailyLimit_AssignsAutoFixedDatesInAdvance()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var today = TodoDay.Today.ToDateTime();
            var targetDate = today.AddDays(value: 4);

            TaskItem taskB = new()
            {
                Id = 502, Title = "Blocked B", ScheduledDate = targetDate, DateSource = DateSource.Manual,
                Status = TaskStatus.Planned, EstimatedMinutes = 180
            };
            TaskItem taskA1 = new()
            {
                Id = 501, Title = "Blocker A1", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned,
                EstimatedMinutes = 300
            };
            TaskItem taskA2 = new()
            {
                Id = 503, Title = "Blocker A2", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned,
                EstimatedMinutes = 300
            };

            context.Tasks.AddRange(entities: [taskA1, taskB, taskA2]);
            context.TaskRelations.Add(entity: new()
                { Id = 5001, SourceTaskId = taskA1.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.TaskRelations.Add(entity: new()
                { Id = 5002, SourceTaskId = taskA2.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 240).Build();

            // Act: Call real application service PlannerService
            plannerService.DistributeTasks(settings: settings);

            var updatedA1 = taskRepo.GetById(id: taskA1.Id);
            var updatedA2 = taskRepo.GetById(id: taskA2.Id);

            // Assert: Blocker assigned AutoFixed date in advance before target task's deadline
            updatedA1.Should().NotBeNull();
            updatedA2.Should().NotBeNull();
            updatedA1.DateSource.Should().Be(expected: DateSource.AutoFixed);
            updatedA1.ScheduledDate.Should().NotBeNull();
            updatedA1.ScheduledDate.Should().BeBefore(expected: targetDate);
        }
    }

    /// <summary>
    /// Tests verification of relation graph persistence without entity tracking errors.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Relations")]
    public class RelationGraphPersistence
    {
        /// <summary>
        /// Verifies that saving a task with attached relations does not throw entity graph tracking exceptions.
        /// </summary>
        [Fact]
        public void SaveTaskWithAttachedRelations_DoesNotThrowEntityTrackingException()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());

            TaskItem taskB = new() { Id = 602, Title = "Target B", Status = TaskStatus.Planned };
            context.Tasks.Add(entity: taskB);
            context.SaveChanges();

            var taskA = new TaskItemBuilder().WithId(id: 601).WithTitle(title: "Source A").Build();
            taskA.Relations.Add(item: new() { SourceTaskId = 601, TargetTaskId = 602, Type = RelationType.Blocks });

            // Act: Call real repository Add method
            var act = () => taskRepo.Add(entity: taskA);

            // Assert: No entity graph tracking exception, relation correctly persisted
            act.Should().NotThrow();
            var savedA = taskRepo.GetById(id: 601);
            savedA.Should().NotBeNull();
            savedA.Relations.Should()
                .ContainSingle(predicate: r => r.TargetTaskId == 602 && r.Type == RelationType.Blocks);
        }
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
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
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
            context.TaskRelations.Add(entity: new()
                { SourceTaskId = taskABlocker.Id, TargetTaskId = taskBBlocked.Id, Type = RelationType.Blocks });
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
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());

            TaskItem taskA1 = new() { Id = 701, Title = "Blocker A1", Status = TaskStatus.Planned };
            TaskItem taskA2 = new() { Id = 702, Title = "Blocker A2", Status = TaskStatus.Planned };
            TaskItem taskB = new() { Id = 703, Title = "Blocked Task B", Status = TaskStatus.Planned };

            context.Tasks.AddRange(entities: [taskA1, taskA2, taskB]);
            context.TaskRelations.Add(entity: new()
                { Id = 7001, SourceTaskId = taskA1.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.TaskRelations.Add(entity: new()
                { Id = 7002, SourceTaskId = taskA2.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
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
                Id = 201, Title = "Blocking Task A", PriorityId = urgentPriority.Id,
                DateSource = DateSource.AutoFlexible,
                Status = TaskStatus.Planned, EstimatedMinutes = 300
            };
            TaskItem taskB = new()
            {
                Id = 202, Title = "Blocked Task B", PriorityId = urgentPriority.Id,
                DateSource = DateSource.AutoFlexible,
                Status = TaskStatus.Planned, EstimatedMinutes = 300
            };
            context.Tasks.AddRange(entities: [taskA, taskB]);

            TaskRelation relation = new()
                { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
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

            TaskRelation relation = new()
                { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
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
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());

            var blocker = new TaskItemBuilder().WithId(id: 201).WithStatus(status: TaskStatus.Planned).Build();
            var notConfiguredTask = new TaskItemBuilder().WithId(id: 202).WithStatus(status: TaskStatus.NotConfigured)
                .Build();

            context.Tasks.AddRange(entities: [blocker, notConfiguredTask]);
            context.TaskRelations.Add(entity: new()
                { SourceTaskId = blocker.Id, TargetTaskId = notConfiguredTask.Id, Type = RelationType.Blocks });
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
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var priorities = context.Priorities.OrderBy(keySelector: p => p.Order).ToList();
            var critical = priorities[index: 0];
            var low = priorities[index: 3];

            var blocker = new TaskItemBuilder().WithId(id: 501).WithPriorityId(priorityId: low.Id)
                .WithStatus(status: TaskStatus.Planned).Build();
            var blocked = new TaskItemBuilder().WithId(id: 502).WithPriorityId(priorityId: low.Id)
                .WithStatus(status: TaskStatus.Planned).Build();
            blocked.PriorityEscalations.Add(item: new()
                { TargetPriorityId = critical.Id, EscalationDate = TodoDay.Today.ToDateTime() });

            context.Tasks.AddRange(entities: [blocker, blocked]);
            context.TaskRelations.Add(entity: new()
                { SourceTaskId = blocker.Id, TargetTaskId = blocked.Id, Type = RelationType.Blocks });
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
    }
}
