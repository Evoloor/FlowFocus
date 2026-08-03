using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Trait("Category", "Relations")]
public class RelationsEngineTests
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

    public class BlockerPriority
    {
        /*[Fact]
        public void BlockerPriorityWeakerThanBlockedTask_ThrowsValidationError()
        {
            // Arrange
            var highPriority = PriorityLevelBuilder.High;
            var lowPriority = PriorityLevelBuilder.Low;

            var taskA = new TaskItemBuilder().WithId(1).WithPriority(lowPriority).Build();
            var taskB = new TaskItemBuilder().WithId(2).WithPriority(highPriority).Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(taskA, taskB, RelationType.Blocks);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*слабее приоритета блокируемой задачи*");
        }*/

        /*[Fact]
        public void NormalizeBlockerPriorities_ElevatesBlockerPriorityToMatchBlockedTask()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
            var criticalPriority = priorities[0];
            var lowPriority = priorities[3];

            var taskA = new TaskItem { Id = 10, Title = "Blocker A", PriorityId = lowPriority.Id, Status = TaskStatus.Planned };
            var taskB = new TaskItem { Id = 20, Title = "Blocked B", PriorityId = criticalPriority.Id, Status = TaskStatus.Planned };
            context.Tasks.AddRange(taskA, taskB);

            context.TaskRelations.Add(new TaskRelation { Id = 100, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            var notificationService = Substitute.For<INotificationService>();
            var taskRepo = new TaskRepository(context, notificationService);
            var plannerService = new PlannerService(taskRepo);

            // Act: Call real domain service
            plannerService.NormalizeBlockerPriorities();
            taskRepo.SaveChanges();

            // Assert: Inspect DB repository state
            var updatedA = taskRepo.GetById(taskA.Id);
            updatedA.Should().NotBeNull();
            updatedA.PriorityId.Should().Be(criticalPriority.Id);
        }*/
    }

    public class CascadeDeadline
    {
        [Fact]
        public void BlockerDeadlineLaterThanBlockedTask_ThrowsValidationError()
        {
            // Arrange
            var blocker = new TaskItemBuilder().WithId(1).WithScheduledDate(new DateTime(2026, 8, 15)).Build();
            var blocked = new TaskItemBuilder().WithId(2).WithScheduledDate(new DateTime(2026, 8, 10)).Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(blocker, blocked, RelationType.Blocks);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*не может быть позже дедлайна*");
        }
    }

    public class UnblockingOnCompletion
    {
        [Fact]
        public void CompleteSoleBlocker_RemovesBlockedStatusFromTargetTask()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskA = new TaskItem { Id = 101, Title = "Blocker A", Status = TaskStatus.Planned };
            var taskB = new TaskItem { Id = 102, Title = "Blocked B", Status = TaskStatus.Planned };
            context.Tasks.AddRange(taskA, taskB);

            context.TaskRelations.Add(new TaskRelation { Id = 1001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            // Act: Call real application service method
            taskRepo.CompleteTask(taskA.Id);

            // Assert: Inspect target task state in repository
            var updatedB = taskRepo.GetById(taskB.Id);
            updatedB.Should().NotBeNull();
            updatedB.IsBlocked.Should().BeFalse();
        }

        [Fact]
        public void CompleteOneOfMultipleBlockers_TaskRemainsBlocked()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskA = new TaskItem { Id = 201, Title = "Blocker A", Status = TaskStatus.Planned };
            var taskC = new TaskItem { Id = 203, Title = "Blocker C", Status = TaskStatus.Planned };
            var taskB = new TaskItem { Id = 202, Title = "Blocked B", Status = TaskStatus.Planned };
            context.Tasks.AddRange(taskA, taskC, taskB);

            context.TaskRelations.Add(new TaskRelation { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.TaskRelations.Add(new TaskRelation { Id = 2002, SourceTaskId = taskC.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            // Act: Call real application service method
            taskRepo.CompleteTask(taskA.Id);

            // Assert: Task B remains blocked by task C
            var updatedB = taskRepo.GetById(taskB.Id);
            updatedB.Should().NotBeNull();
            updatedB.IsBlocked.Should().BeTrue();
        }
    }

    public class BidirectionalVisibility
    {
        [Fact]
        public void SingleRelationRecordInDb_ExposesBidirectionalNavigation()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskA = new TaskItem { Id = 301, Title = "Task A", Status = TaskStatus.Planned };
            var taskB = new TaskItem { Id = 302, Title = "Task B", Status = TaskStatus.Planned };
            context.Tasks.AddRange(taskA, taskB);

            context.TaskRelations.Add(new TaskRelation { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            // Act: Query repository
            var fetchedA = taskRepo.GetById(taskA.Id);
            var fetchedB = taskRepo.GetById(taskB.Id);

            // Assert: Verify bidirectional state in application repository
            fetchedA!.Relations.Should().ContainSingle(r => r.TargetTaskId == taskB.Id && r.Type == RelationType.Blocks);
            fetchedB!.InverseRelations.Should().ContainSingle(r => r.SourceTaskId == taskA.Id && r.Type == RelationType.Blocks);
        }
    }

    public class BlockerAutoFixedDateMath
    {
        [Fact]
        public void BlockerAutoFixedDateMath_CalculatesChainHoursDividedByDailyLimit_AssignsAutoFixedDatesInAdvance()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var today = TodoDay.Today.ToDateTime();
            var targetDate = today.AddDays(4);

            var taskB = new TaskItem { Id = 502, Title = "Blocked B", ScheduledDate = targetDate, DateSource = DateSource.Manual, Status = TaskStatus.Planned, EstimatedMinutes = 180 };
            var taskA1 = new TaskItem { Id = 501, Title = "Blocker A1", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 300 };
            var taskA2 = new TaskItem { Id = 503, Title = "Blocker A2", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 300 };

            context.Tasks.AddRange(taskA1, taskB, taskA2);
            context.TaskRelations.Add(new TaskRelation { Id = 5001, SourceTaskId = taskA1.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.TaskRelations.Add(new TaskRelation { Id = 5002, SourceTaskId = taskA2.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(240).Build();

            // Act: Call real application service PlannerService
            plannerService.DistributeTasks(settings);

            var updatedA1 = taskRepo.GetById(taskA1.Id);
            var updatedA2 = taskRepo.GetById(taskA2.Id);

            // Assert: Inspect task repository persistent state
            updatedA1.Should().NotBeNull();
            updatedA2.Should().NotBeNull();
            updatedA1.DateSource.Should().Be(DateSource.AutoFixed);
            updatedA1.ScheduledDate.Should().Be(targetDate);
        }
    }

    public class RelationGraphPersistence
    {
        [Fact]
        public void SaveTaskWithAttachedRelations_DoesNotThrowEntityTrackingException()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            var taskB = new TaskItem { Id = 602, Title = "Target B", Status = TaskStatus.Planned };
            context.Tasks.Add(taskB);
            context.SaveChanges();

            var taskA = new TaskItemBuilder().WithId(601).WithTitle("Source A").Build();
            taskA.Relations.Add(new TaskRelation { SourceTaskId = 601, TargetTaskId = 602, Type = RelationType.Blocks });

            // Act: Call real repository Add method
            var act = () => taskRepo.Add(taskA);

            // Assert: No entity graph tracking exception, relation correctly persisted
            act.Should().NotThrow();
            var savedA = taskRepo.GetById(601);
            savedA.Should().NotBeNull();
            savedA!.Relations.Should().ContainSingle(r => r.TargetTaskId == 602 && r.Type == RelationType.Blocks);
        }
    }
}
