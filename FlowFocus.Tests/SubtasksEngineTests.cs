using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FluentAssertions;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for subtask aggregation, list isolation, edit field truncation, and hierarchy validation.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Domain")]
[Collection("StaticState")]
public class SubtasksEngineTests : IntegrationTestBase
{
    /// <summary>
    /// Verifies that total estimated minutes and complexity aggregate parent and subtasks recursively.
    /// </summary>
    [Fact]
    public void CalculateTotalMinutesAndComplexity_AggregatesParentAndSubtasksFromRepository()
    {
        // Arrange
        var subtask1 = new TaskItemBuilder().WithId(101).WithEstimatedMinutes(15).WithComplexity(5).Build();
        var subtask2 = new TaskItemBuilder().WithId(102).WithEstimatedMinutes(45).WithComplexity(15).Build();

        var parent = new TaskItemBuilder()
            .WithId(100)
            .WithEstimatedMinutes(30)
            .WithComplexity(10)
            .WithSubtask(subtask1)
            .WithSubtask(subtask2)
            .Build();

        // Act
        TaskRepo.Add(parent);
        var savedParent = TaskRepo.GetById(parent.Id);

        // Assert
        savedParent.Should().NotBeNull();
        savedParent!.TotalEstimatedMinutes.Should().Be(90);
        savedParent.TotalComplexity.Should().Be(30);
    }

    /// <summary>
    /// Verifies that repository root queries exclude subtasks with non-null ParentTaskId.
    /// </summary>
    [Fact]
    public void RepositoryRootQuery_ExcludesSubtasksWithNonNullParentId()
    {
        // Arrange
        (var mainTask, _) = TaskItemBuilder.CreateParentWithSubtasks(1, 200);

        TaskRepo.Add(mainTask);

        // Act
        var rootTasks = TaskRepo.GetAll().Where(t => t.ParentTaskId == null).ToList();

        // Assert
        rootTasks.Should().ContainSingle();
        rootTasks.First().Id.Should().Be(200);
    }

    /// <summary>
    /// Verifies that subtask models expose only valid allowed subtask properties.
    /// </summary>
    [Fact]
    public void SubtaskModel_ExposesOnlyAllowedSubtaskFields()
    {
        // Arrange & Act
        var subtask = new TaskItemBuilder()
            .WithTitle("Subtask Title")
            .WithInterest(8)
            .WithComplexity(20)
            .WithEstimatedMinutes(25)
            .WithParentTaskId(400)
            .Build();

        // Assert
        subtask.IsSubtask.Should().BeTrue();
        subtask.Title.Should().Be("Subtask Title");
        subtask.Interest.Should().Be(8);
        subtask.Complexity.Should().Be(20);
        subtask.EstimatedMinutes.Should().Be(25);
        subtask.ParentTaskId.Should().Be(400);
        subtask.IsRecurring.Should().BeFalse();
        subtask.ScheduledDate.Should().BeNull();
    }

    /// <summary>
    /// Verifies that assigning a higher priority to a subtask than its parent throws a validation exception.
    /// </summary>
    [Fact]
    public void SubtaskPriority_ExceedingParentPriority_ThrowsValidationError()
    {
        // Arrange
        var parentPriority = PriorityLevelBuilder.Medium;
        var subtaskPriority = PriorityLevelBuilder.High;
        var parentTask = new TaskItemBuilder()
            .WithId(10)
            .WithPriority(parentPriority)
            .Build();
        var subtask = new TaskItemBuilder()
            .WithId(11)
            .WithPriority(subtaskPriority)
            .WithParentTask(parentTask)
            .Build();

        // Act
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask: parentTask, childTask: subtask);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedWildcardPattern: "*приоритет не может быть выше приоритета родительской*");
    }

    /// <summary>
    /// Verifies that subtask's scheduled date is live-bound to its parent task's scheduled date.
    /// </summary>
    [Fact]
    public void SubtaskDate_LiveBindsToParentTaskDate()
    {
        // Arrange
        DateTime initialDate = new(2026, 8, 10);
        var parentTask = new TaskItemBuilder()
            .WithId(20)
            .WithScheduledDate(initialDate, DateSource.Manual)
            .Build();

        var subtask = new TaskItemBuilder()
            .WithId(21)
            .WithParentTask(parentTask)
            .Build();

        // Assert initial live binding
        subtask.ScheduledDate.Should().Be(initialDate);
        subtask.DateSource.Should().Be(DateSource.Manual);

        // Act - change parent date
        DateTime updatedDate = new(2026, 8, 15);
        parentTask.ScheduledDate = updatedDate;
        parentTask.DateSource = DateSource.AutoFixed;

        // Assert live update reflection
        subtask.ScheduledDate.Should().Be(updatedDate);
        subtask.DateSource.Should().Be(DateSource.AutoFixed);

        // Act - attempt to change date via subtask
        DateTime newDateViaSubtask = new(2026, 8, 20);
        subtask.ScheduledDate = newDateViaSubtask;
        subtask.DateSource = DateSource.Manual;

        // Assert - Parent task must NOT be updated through subtask under any circumstances!
        parentTask.ScheduledDate.Should().Be(updatedDate);
        parentTask.DateSource.Should().Be(DateSource.AutoFixed);

        // Subtask date must continue to live-bind to parent task date
        subtask.ScheduledDate.Should().Be(updatedDate);
        subtask.DateSource.Should().Be(DateSource.AutoFixed);
    }

    /// <summary>
    /// Verifies that assigning date or date source to a subtask NEVER modifies the parent task or sibling subtasks.
    /// </summary>
    [Fact]
    public void Subtask_DateOrSourceChange_NeverModifiesParentTaskOrSiblingSubtasks()
    {
        // Arrange
        DateTime initialDate = new(2026, 8, 10);
        var sub1 = new TaskItemBuilder().WithId(101).WithTitle("Subtask 1").Build();
        var sub2 = new TaskItemBuilder().WithId(102).WithTitle("Subtask 2").Build();
        var sub3 = new TaskItemBuilder().WithId(103).WithTitle("Subtask 3").Build();

        var parentTask = new TaskItemBuilder()
            .WithId(100)
            .WithScheduledDate(initialDate, DateSource.AutoFixed)
            .WithSubtask(sub1)
            .WithSubtask(sub2)
            .WithSubtask(sub3)
            .Build();

        // Initial state: all subtasks live-bind to parent
        sub1.ScheduledDate.Should().Be(initialDate);
        sub2.ScheduledDate.Should().Be(initialDate);
        sub3.ScheduledDate.Should().Be(initialDate);

        // Act - aggressively try to change date and date source via subtask 1
        DateTime attemptDate = new(2026, 12, 31);
        sub1.ScheduledDate = attemptDate;
        sub1.DateSource = DateSource.Manual;

        // Assert - parent task is COMPLETELY UNTOUCHED
        parentTask.ScheduledDate.Should().Be(initialDate);
        parentTask.DateSource.Should().Be(DateSource.AutoFixed);

        // Assert - sibling subtasks are COMPLETELY UNTOUCHED
        sub2.ScheduledDate.Should().Be(initialDate);
        sub2.DateSource.Should().Be(DateSource.AutoFixed);
        sub3.ScheduledDate.Should().Be(initialDate);
        sub3.DateSource.Should().Be(DateSource.AutoFixed);

        // Assert - subtask 1 continues to live-bind to parent date
        sub1.ScheduledDate.Should().Be(initialDate);
        sub1.DateSource.Should().Be(DateSource.AutoFixed);

        // Act 2 - updating parent task date propagates down to all subtasks
        DateTime newParentDate = new(2026, 8, 25);
        parentTask.ScheduledDate = newParentDate;
        parentTask.DateSource = DateSource.Manual;

        sub1.ScheduledDate.Should().Be(newParentDate);
        sub1.DateSource.Should().Be(DateSource.Manual);
        sub2.ScheduledDate.Should().Be(newParentDate);
        sub2.DateSource.Should().Be(DateSource.Manual);
        sub3.ScheduledDate.Should().Be(newParentDate);
        sub3.DateSource.Should().Be(DateSource.Manual);
    }

    /// <summary>
    /// Verifies that updating a subtask directly in repository never mutates the parent task's schedule.
    /// </summary>
    [Fact]
    public void Subtask_UpdateInRepository_NeverModifiesParentTask()
    {
        // Arrange
        DateTime initialDate = new(2026, 9, 1);
        var subtask = new TaskItemBuilder().WithId(501).Build();
        var parent = new TaskItemBuilder()
            .WithId(500)
            .WithScheduledDate(initialDate, DateSource.Manual)
            .WithSubtask(subtask)
            .Build();

        TaskRepo.Add(parent);

        // Act - attempt to change date on retrieved subtask and update via repo
        var retrievedSubtask = TaskRepo.GetById(subtask.Id)!;
        retrievedSubtask.ScheduledDate = new DateTime(2026, 9, 30);
        retrievedSubtask.DateSource = DateSource.AutoFixed;
        TaskRepo.Update(retrievedSubtask);

        // Assert - parent task in repository must NOT be modified
        var refreshedParent = TaskRepo.GetById(parent.Id)!;
        refreshedParent.ScheduledDate.Should().Be(initialDate);
        refreshedParent.DateSource.Should().Be(DateSource.Manual);

        // Subtask still live-binds to parent's date
        var refreshedSubtask = TaskRepo.GetById(subtask.Id)!;
        refreshedSubtask.ScheduledDate.Should().Be(initialDate);
        refreshedSubtask.DateSource.Should().Be(DateSource.Manual);
    }

    /// <summary>
    /// Verifies that subtask live date binding persists and functions correctly through repository operations.
    /// </summary>
    [Fact]
    public void SubtaskDate_LiveBindsInRepository()
    {
        // Arrange
        DateTime initialDate = new(2026, 9, 1);
        var subtask = new TaskItemBuilder()
            .WithId(501)
            .Build();

        var parent = new TaskItemBuilder()
            .WithId(500)
            .WithScheduledDate(initialDate, DateSource.Manual)
            .WithSubtask(subtask)
            .Build();

        TaskRepo.Add(parent);

        // Act
        DateTime updatedDate = new(2026, 9, 10);
        TaskRepo.UpdateTaskSchedule(parent.Id, updatedDate, DateSource.AutoFixed);

        var retrievedParent = TaskRepo.GetById(parent.Id);
        var retrievedSubtask = TaskRepo.GetById(subtask.Id);

        // Assert
        retrievedParent.Should().NotBeNull();
        retrievedParent!.ScheduledDate.Should().Be(updatedDate);

        retrievedSubtask.Should().NotBeNull();
        retrievedSubtask!.ScheduledDate.Should().Be(updatedDate);
        retrievedSubtask.DateSource.Should().Be(DateSource.AutoFixed);
    }

    /// <summary>
    /// Verifies that completing a parent task does not affect the statuses of its subtasks.
    /// </summary>
    [Fact]
    public void CompleteParentTask_DoesNotAffectSubtaskStatuses()
    {
        // Arrange
        DateTime today = TodoDay.Today.ToDateTime();
        var subtaskPlanned = new TaskItemBuilder()
            .WithId(601)
            .WithTitle("Planned Subtask")
            .Build();

        var subtaskIrrelevant = new TaskItemBuilder()
            .WithId(602)
            .WithTitle("Irrelevant Subtask")
            .Build();

        var subtaskCompleted = new TaskItemBuilder()
            .WithId(603)
            .WithTitle("Completed Subtask")
            .Build();

        var parent = new TaskItemBuilder()
            .WithId(600)
            .WithTitle("Parent Task")
            .WithScheduledDate(today, DateSource.AutoFixed)
            .WithStatus(TaskStatus.Planned)
            .WithSubtask(subtaskPlanned)
            .WithSubtask(subtaskIrrelevant)
            .WithSubtask(subtaskCompleted)
            .Build();

        TaskRepo.Add(parent);

        // Explicitly set different statuses to subtasks
        TaskRepo.MarkIrrelevant(subtaskIrrelevant.Id);
        TaskRepo.CompleteTask(subtaskCompleted.Id);

        // Act - complete the parent task
        TaskRepo.CompleteTask(parent.Id);

        // Assert - parent is completed
        var retrievedParent = TaskRepo.GetById(parent.Id);
        retrievedParent.Should().NotBeNull();
        retrievedParent!.Status.Should().Be(TaskStatus.Completed);

        // Assert - subtasks retain their exact original statuses
        var retrievedPlanned = TaskRepo.GetById(subtaskPlanned.Id);
        var retrievedIrrelevant = TaskRepo.GetById(subtaskIrrelevant.Id);
        var retrievedCompleted = TaskRepo.GetById(subtaskCompleted.Id);

        retrievedPlanned.Should().NotBeNull();
        retrievedPlanned!.Status.Should().Be(TaskStatus.Planned);

        retrievedIrrelevant.Should().NotBeNull();
        retrievedIrrelevant!.Status.Should().Be(TaskStatus.Irrelevant);

        retrievedCompleted.Should().NotBeNull();
        retrievedCompleted!.Status.Should().Be(TaskStatus.Completed);
    }
}
