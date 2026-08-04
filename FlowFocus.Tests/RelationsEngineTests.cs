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
            var blocker = new TaskItemBuilder().WithId(id: 1).WithScheduledDate(date: new DateTime(year: 2026, month: 8, day: 15)).Build();
            var blocked = new TaskItemBuilder().WithId(id: 2).WithScheduledDate(date: new DateTime(year: 2026, month: 8, day: 10)).Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: blocker, targetTask: blocked, type: RelationType.Blocks);

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

            context.TaskRelations.Add(entity: new() { Id = 1001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

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

            context.TaskRelations.Add(entity: new() { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.TaskRelations.Add(entity: new() { Id = 2002, SourceTaskId = taskC.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

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

            context.TaskRelations.Add(entity: new() { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            // Act: Query repository
            var fetchedA = taskRepo.GetById(id: taskA.Id);
            var fetchedB = taskRepo.GetById(id: taskB.Id);

            // Assert: Verify bidirectional state in application repository
            fetchedA!.Relations.Should().ContainSingle(predicate: r => r.TargetTaskId == taskB.Id && r.Type == RelationType.Blocks);
            fetchedB!.InverseRelations.Should().ContainSingle(predicate: r => r.SourceTaskId == taskA.Id && r.Type == RelationType.Blocks);
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
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var today = TodoDay.Today.ToDateTime();
            var targetDate = today.AddDays(value: 4);

            TaskItem taskB = new() { Id = 502, Title = "Blocked B", ScheduledDate = targetDate, DateSource = DateSource.Manual, Status = TaskStatus.Planned, EstimatedMinutes = 180 };
            TaskItem taskA1 = new() { Id = 501, Title = "Blocker A1", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 300 };
            TaskItem taskA2 = new() { Id = 503, Title = "Blocker A2", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 300 };

            context.Tasks.AddRange(entities: [taskA1, taskB, taskA2]);
            context.TaskRelations.Add(entity: new() { Id = 5001, SourceTaskId = taskA1.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.TaskRelations.Add(entity: new() { Id = 5002, SourceTaskId = taskA2.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
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
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

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
            savedA.Relations.Should().ContainSingle(predicate: r => r.TargetTaskId == 602 && r.Type == RelationType.Blocks);
        }
    }
}
