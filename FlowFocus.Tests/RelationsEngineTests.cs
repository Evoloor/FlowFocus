using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for task relations, blocker cascades, unblocking mechanics, and graph persistence.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Relations")]
[Collection("StaticState")]
public class RelationsEngineTests : IntegrationTestBase
{
    /// <summary>
    /// Verifies that when a blocked task's priority escalates to critical (Priority 1),
    /// its blocker cascades to critical priority and legitimately bypasses daily limits to be scheduled today.
    /// </summary>
    [Fact]
    public void EscalatedBlockerChain_LegitimatelyBypassesLimits_AsCriticalPriority()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var critical = priorities[0];
        var low = priorities[3];

        // Забиваем день "мусорной" задачей, чтобы лимит был исчерпан
        var fillerTask = new TaskItemBuilder().WithId(10).WithPriorityId(low.Id)
            .WithEstimatedMinutes(240).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        // Блокер (низкий приоритет)
        var blocker = new TaskItemBuilder().WithId(11).WithPriorityId(low.Id)
            .WithEstimatedMinutes(120).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
        
        // Заблокированная (низкий приоритет, но с правилом повышения СЕГОДНЯ)
        var blocked = new TaskItemBuilder().WithId(12).WithPriorityId(low.Id)
            .WithEstimatedMinutes(120).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
        blocked.PriorityEscalations.Add(new PriorityEscalation { TargetPriorityId = critical.Id, EscalationDate = TodoDay.Today.ToDateTime() });

        Context.Tasks.AddRange(fillerTask, blocker, blocked);
        Context.TaskRelations.Add(new TaskRelation { SourceTaskId = 11, TargetTaskId = 12, Type = RelationType.Blocks });
        Context.SaveChanges();

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(240).Build();

        // Act: Полный цикл планировщика (Актуализация -> Нормализация -> Распределение)
        PlannerService.ActualizePriorities();
        PlannerService.NormalizeBlockerPriorities();
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedBlocker = TaskRepo.GetById(11);
        var savedBlocked = TaskRepo.GetById(12);

        savedBlocker!.PriorityId.Should().Be(critical.Id, "Блокер должен каскадно повыситься");
        savedBlocker.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime(), "Критический блокер должен встать на сегодня в обход лимита");
        savedBlocked!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime(), "Критическая заблокированная должна встать на сегодня в обход лимита");
    }

    /// <summary>
    /// Verifies that setting a blocker deadline later than the blocked task deadline throws a validation exception.
    /// </summary>
    [Fact]
    public void BlockerDeadlineLaterThanBlockedTask_ThrowsValidationError()
    {
        // Arrange
        var blocker = new TaskItemBuilder().WithId(1)
            .WithScheduledDate(new DateTime(2026, 8, 15)).Build();
        var blocked = new TaskItemBuilder().WithId(2)
            .WithScheduledDate(new DateTime(2026, 8, 10)).Build();

        // Act: Call real domain validator
        var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: blocker, targetTask: blocked, type: RelationType.Blocks);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedWildcardPattern: "*не может быть позже дедлайна*");
    }

    /// <summary>
    /// Verifies that completing the sole blocker removes blocked status from target task.
    /// </summary>
    [Fact]
    public void CompleteSoleBlocker_RemovesBlockedStatusFromTargetTask()
    {
        // Arrange
        var (blocker, blocked) = TaskItemBuilder.CreateBlockedChain(101, 102);
        Context.Tasks.AddRange(blocker, blocked);
        Context.TaskRelations.Add(new TaskRelation { Id = 1001, SourceTaskId = blocker.Id, TargetTaskId = blocked.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act
        TaskRepo.CompleteTask(blocker.Id);

        // Assert
        var updatedB = TaskRepo.GetById(blocked.Id);
        updatedB.Should().NotBeNull();
        updatedB!.IsBlocked.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that completing one of multiple blockers leaves target task blocked by remaining blockers.
    /// </summary>
    [Fact]
    public void CompleteOneOfMultipleBlockers_TaskRemainsBlocked()
    {
        // Arrange
        TaskItem taskA = new() { Id = 201, Title = "Blocker A", Status = TaskStatus.Planned };
        TaskItem taskC = new() { Id = 203, Title = "Blocker C", Status = TaskStatus.Planned };
        TaskItem taskB = new() { Id = 202, Title = "Blocked B", Status = TaskStatus.Planned };
        Context.Tasks.AddRange(taskA, taskC, taskB);

        Context.TaskRelations.Add(new TaskRelation { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        Context.TaskRelations.Add(new TaskRelation { Id = 2002, SourceTaskId = taskC.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act
        TaskRepo.CompleteTask(taskA.Id);

        // Assert
        var updatedB = TaskRepo.GetById(taskB.Id);
        updatedB.Should().NotBeNull();
        updatedB!.IsBlocked.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a single relation record in DB exposes bidirectional navigation properties in repository queries.
    /// </summary>
    [Fact]
    public void SingleRelationRecordInDb_ExposesBidirectionalNavigation()
    {
        // Arrange
        TaskItem taskA = new() { Id = 301, Title = "Task A", Status = TaskStatus.Planned };
        TaskItem taskB = new() { Id = 302, Title = "Task B", Status = TaskStatus.Planned };
        Context.Tasks.AddRange(taskA, taskB);

        Context.TaskRelations.Add(new TaskRelation { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act
        var fetchedA = TaskRepo.GetById(taskA.Id);
        var fetchedB = TaskRepo.GetById(taskB.Id);

        // Assert
        fetchedA!.Relations.Should().ContainSingle(r => r.TargetTaskId == taskB.Id && r.Type == RelationType.Blocks);
        fetchedB!.InverseRelations.Should().ContainSingle(r => r.SourceTaskId == taskA.Id && r.Type == RelationType.Blocks);
    }

    /// <summary>
    /// Verifies that saving a task with attached relations does not throw entity graph tracking exceptions.
    /// </summary>
    [Fact]
    public void SaveTaskWithAttachedRelations_DoesNotThrowEntityTrackingException()
    {
        // Arrange
        TaskItem taskB = new() { Id = 602, Title = "Target B", Status = TaskStatus.Planned };
        Context.Tasks.Add(taskB);
        Context.SaveChanges();

        var taskA = new TaskItemBuilder().WithId(601).WithTitle("Source A").Build();
        taskA.Relations.Add(new TaskRelation { SourceTaskId = 601, TargetTaskId = 602, Type = RelationType.Blocks });

        // Act
        var act = () => TaskRepo.Add(taskA);

        // Assert
        act.Should().NotThrow();
        var savedA = TaskRepo.GetById(601);
        savedA.Should().NotBeNull();
        savedA!.Relations.Should().ContainSingle(r => r.TargetTaskId == 602 && r.Type == RelationType.Blocks);
    }

    /// <summary>
    /// Verifies that DistributeTasks always schedules blockers before or on the same day as blocked tasks.
    /// </summary>
    [Fact]
    public void DistributeTasks_AlwaysSchedulesBlockerBeforeOrOnSameDayAsBlockedTask()
    {
        // Arrange
        var taskABlocker = new TaskItemBuilder()
            .WithId(601)
            .WithTitle("Blocker A")
            .WithInterest(1)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var taskBBlocked = new TaskItemBuilder()
            .WithId(602)
            .WithTitle("Blocked B")
            .WithInterest(10)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        Context.Tasks.AddRange(taskABlocker, taskBBlocked);
        Context.TaskRelations.Add(new TaskRelation { SourceTaskId = taskABlocker.Id, TargetTaskId = taskBBlocked.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        taskABlocker.EstimatedMinutes = 45;
        taskBBlocked.EstimatedMinutes = 45;

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(60).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var updatedA = TaskRepo.GetById(taskABlocker.Id);
        var updatedB = TaskRepo.GetById(taskBBlocked.Id);

        updatedA.Should().NotBeNull();
        updatedB.Should().NotBeNull();
        updatedB!.ScheduledDate.Should().BeOnOrAfter(updatedA!.ScheduledDate!.Value);
    }

    /// <summary>
    /// Verifies that completing a blocked task removes incoming blocker relations.
    /// </summary>
    [Fact]
    public void CompleteOrMarkIrrelevantBlockedTask_RemovesIncomingBlockerRelations()
    {
        // Arrange
        TaskItem taskA1 = new() { Id = 701, Title = "Blocker A1", Status = TaskStatus.Planned };
        TaskItem taskA2 = new() { Id = 702, Title = "Blocker A2", Status = TaskStatus.Planned };
        TaskItem taskB = new() { Id = 703, Title = "Blocked Task B", Status = TaskStatus.Planned };

        Context.Tasks.AddRange(taskA1, taskA2, taskB);
        Context.TaskRelations.Add(new TaskRelation { Id = 7001, SourceTaskId = taskA1.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        Context.TaskRelations.Add(new TaskRelation { Id = 7002, SourceTaskId = taskA2.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act
        TaskRepo.CompleteTask(taskB.Id);

        // Assert
        var savedB = TaskRepo.GetById(taskB.Id);
        var remainingRelationsToB = Context.TaskRelations
            .Where(r => r.TargetTaskId == taskB.Id && r.Type == RelationType.Blocks)
            .ToList();

        savedB.Should().NotBeNull();
        savedB!.Status.Should().Be(TaskStatus.Completed);
        remainingRelationsToB.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that blocked task scheduled date is not earlier than blocking task scheduled date.
    /// </summary>
    [Fact]
    public void DistributeTasks_BlockedTaskDateIsNotEarlierThanBlockingTaskDate()
    {
        // Arrange
        var urgentPriority = Context.Priorities.OrderBy(p => p.Order).First();

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
        Context.Tasks.AddRange(taskA, taskB);

        Context.TaskRelations.Add(new TaskRelation { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        UserSettings settings = new() { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };

        // Act
        PlannerService.DistributeTasks(settings);

        var updatedTaskA = TaskRepo.GetById(taskA.Id);
        var updatedTaskB = TaskRepo.GetById(taskB.Id);

        // Assert
        updatedTaskA?.ScheduledDate.Should().NotBeNull();
        updatedTaskB?.ScheduledDate.Should().NotBeNull();
        updatedTaskB!.ScheduledDate.Should().BeOnOrAfter(updatedTaskA!.ScheduledDate!.Value);
    }

    /// <summary>
    /// Verifies that approaching deadlines convert blockers to AutoFixed when buffer is exhausted.
    /// </summary>
    [Fact]
    public void ApproachingDeadline_ConvertsBlockersToAutoFixedWhenBufferExhausted()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var tomorrow = today.AddDays(1);

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
        Context.Tasks.AddRange(taskA, taskB);

        Context.TaskRelations.Add(new TaskRelation { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        UserSettings settings = new() { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };

        // Act
        PlannerService.DistributeTasks(settings);

        var updatedTaskA = TaskRepo.GetById(taskA.Id);

        // Assert
        updatedTaskA.Should().NotBeNull();
        updatedTaskA!.DateSource.Should().Be(DateSource.AutoFixed);
        updatedTaskA.ScheduledDate.Should().Be(tomorrow);
    }

    /// <summary>
    /// Verifies that unconfigured tasks transition to Blocked status when saved with active blockers.
    /// </summary>
    [Fact]
    public void NotConfiguredTask_WhenSavedWithActiveBlocker_TransitionsToBlockedStatus()
    {
        // Arrange
        var blocker = new TaskItemBuilder().WithId(201).WithStatus(TaskStatus.Planned).Build();
        var notConfiguredTask = new TaskItemBuilder().WithId(202).WithStatus(TaskStatus.NotConfigured).Build();

        Context.Tasks.AddRange(blocker, notConfiguredTask);
        Context.TaskRelations.Add(new TaskRelation { SourceTaskId = blocker.Id, TargetTaskId = notConfiguredTask.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act 1: Verify relation addition did not automatically alter status
        var taskBeforeEdit = TaskRepo.GetById(notConfiguredTask.Id);
        taskBeforeEdit!.Status.Should().Be(TaskStatus.NotConfigured);

        // Act 2: User configures task and saves
        taskBeforeEdit.Title = "Настроенное название";
        taskBeforeEdit.Status = TaskStatus.Planned;

        TaskRepo.Update(taskBeforeEdit);

        // Assert: Repository/Service forces status to "Blocked"
        var savedTask = TaskRepo.GetById(notConfiguredTask.Id);
        savedTask!.Status.Should().Be(TaskStatus.Blocked);
    }

    /// <summary>
    /// Verifies that priority escalation of a blocked task forces cascade priority increases on blockers.
    /// </summary>
    [Fact]
    public void AutoEscalation_OfBlockedTask_ForcesCascadePriorityIncreaseOnBlockers()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var critical = priorities[0];
        var low = priorities[3];

        var blocker = new TaskItemBuilder().WithId(501).WithPriorityId(low.Id)
            .WithStatus(TaskStatus.Planned).Build();
        var blocked = new TaskItemBuilder().WithId(502).WithPriorityId(low.Id)
            .WithStatus(TaskStatus.Planned).Build();
        blocked.PriorityEscalations.Add(new PriorityEscalation
            { TargetPriorityId = critical.Id, EscalationDate = TodoDay.Today.ToDateTime() });

        Context.Tasks.AddRange(blocker, blocked);
        Context.TaskRelations.Add(new TaskRelation { SourceTaskId = blocker.Id, TargetTaskId = blocked.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act
        PlannerService.ActualizePriorities();
        TaskRepo.SaveChanges();

        // Assert
        var updatedBlocker = TaskRepo.GetById(blocker.Id);
        var updatedBlocked = TaskRepo.GetById(blocked.Id);

        updatedBlocked!.PriorityId.Should().Be(critical.Id);
        updatedBlocker!.PriorityId.Should().Be(critical.Id);
    }
}
