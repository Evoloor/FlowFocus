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
/// Unit tests for subtask aggregation, list isolation, model validation, and hierarchy.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Domain")]
[Collection("StaticState")]
public class SubtasksEngineTests : IntegrationTestBase
{
    /// <summary>
    /// Verifies that total estimated minutes and complexity aggregate parent and subtasks.
    /// </summary>
    [Fact]
    public void CalculateTotalMinutesAndComplexity_AggregatesParentAndSubtasks()
    {
        // Arrange
        var subtask1 = new SubtaskItemBuilder().WithId(101).WithEstimatedMinutes(15).WithComplexity(5).Build();
        var subtask2 = new SubtaskItemBuilder().WithId(102).WithEstimatedMinutes(45).WithComplexity(15).Build();

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
    /// Verifies that GetAll returns only top-level tasks (no subtasks mixed in).
    /// </summary>
    [Fact]
    public void GetAll_ReturnsOnlyTopLevelTasks()
    {
        // Arrange
        (var mainTask, _) = TaskItemBuilder.CreateParentWithSubtasks(2, 200);
        TaskRepo.Add(mainTask);

        // Act
        var allTasks = TaskRepo.GetAll();

        // Assert — only the parent task should be returned, subtasks are nested
        allTasks.Should().ContainSingle();
        allTasks.First().Id.Should().Be(200);
        allTasks.First().Subtasks.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that SubtaskItem model only has allowed fields (no ScheduledDate, DateSource, IsRecurring etc.)
    /// </summary>
    [Fact]
    public void SubtaskModel_HasOnlyAllowedFields()
    {
        // Arrange & Act
        var subtask = new SubtaskItemBuilder()
            .WithTitle("Subtask Title")
            .WithInterest(8)
            .WithComplexity(20)
            .WithEstimatedMinutes(25)
            .WithParentTaskId(400)
            .Build();

        // Assert — SubtaskItem has all expected fields
        subtask.Title.Should().Be("Subtask Title");
        subtask.Interest.Should().Be(8);
        subtask.Complexity.Should().Be(20);
        subtask.EstimatedMinutes.Should().Be(25);
        subtask.ParentTaskId.Should().Be(400);

        // SubtaskItem should NOT have ScheduledDate, DateSource, IsRecurring, PriorityId etc.
        // (These are compile-time guarantees via the type system now)
        subtask.Should().BeOfType<SubtaskItem>();
        subtask.Should().NotBeAssignableTo<TaskItem>();
    }

    /// <summary>
    /// Verifies that ValidateSubtaskParent rejects a subtask referencing a different parent.
    /// </summary>
    [Fact]
    public void SubtaskValidation_MismatchedParent_ThrowsValidationError()
    {
        // Arrange
        var parentTask = new TaskItemBuilder().WithId(10).Build();
        var subtask = new SubtaskItemBuilder().WithId(10).WithParentTaskId(99).Build();

        // Act
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask, subtask);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedWildcardPattern: "*привязана к другой родительской задаче*");
    }

    /// <summary>
    /// Verifies that toggling a subtask status cycles between Planned and Completed with correct CompletedDate.
    /// </summary>
    [Fact]
    public void ToggleSubtaskStatus_CyclesStatusAndCompletedDate()
    {
        // Arrange
        var subtask = new SubtaskItemBuilder().WithId(101).WithTitle("Subtask to Toggle").Build();
        var parent = new TaskItemBuilder().WithId(100).WithSubtask(subtask).Build();
        TaskRepo.Add(parent);

        // Act 1 - toggle from Planned to Completed
        TaskRepo.ToggleSubtaskStatus(subtask.Id);

        // Assert 1
        var retrievedParent1 = TaskRepo.GetById(parent.Id)!;
        var retrievedSub1 = retrievedParent1.Subtasks.First(s => s.Id == subtask.Id);
        retrievedSub1.Status.Should().Be(TaskStatus.Completed);
        retrievedSub1.CompletedDate.Should().NotBeNull();
        retrievedParent1.Status.Should().Be(TaskStatus.Planned, "completing a subtask does not complete the parent");

        // Act 2 - toggle back from Completed to Planned
        TaskRepo.ToggleSubtaskStatus(subtask.Id);

        // Assert 2
        var retrievedParent2 = TaskRepo.GetById(parent.Id)!;
        var retrievedSub2 = retrievedParent2.Subtasks.First(s => s.Id == subtask.Id);
        retrievedSub2.Status.Should().Be(TaskStatus.Planned);
        retrievedSub2.CompletedDate.Should().BeNull();
    }

    /// <summary>
    /// Verifies CompleteSubtask and ReopenSubtask methods.
    /// </summary>
    [Fact]
    public void CompleteSubtask_And_ReopenSubtask_WorkCorrectly()
    {
        // Arrange
        var subtask = new SubtaskItemBuilder().WithId(201).WithTitle("Subtask").Build();
        var parent = new TaskItemBuilder().WithId(200).WithSubtask(subtask).Build();
        TaskRepo.Add(parent);

        // Act - complete
        TaskRepo.CompleteSubtask(subtask.Id);
        var subAfterComplete = TaskRepo.GetById(parent.Id)!.Subtasks.First(s => s.Id == subtask.Id);
        subAfterComplete.Status.Should().Be(TaskStatus.Completed);
        subAfterComplete.CompletedDate.Should().NotBeNull();

        // Act - reopen
        TaskRepo.ReopenSubtask(subtask.Id);
        var subAfterReopen = TaskRepo.GetById(parent.Id)!.Subtasks.First(s => s.Id == subtask.Id);
        subAfterReopen.Status.Should().Be(TaskStatus.Planned);
        subAfterReopen.CompletedDate.Should().BeNull();
    }

    /// <summary>
    /// Verifies that subtasks are stored in the Subtasks table, not the Tasks table.
    /// </summary>
    [Fact]
    public void Subtasks_StoredInSeparateCollection()
    {
        // Arrange
        var subtask = new SubtaskItemBuilder().WithId(501).WithTitle("My Subtask").Build();
        var parent = new TaskItemBuilder()
            .WithId(500)
            .WithScheduledDate(new DateTime(2026, 9, 1), DateSource.Manual)
            .WithSubtask(subtask)
            .Build();

        TaskRepo.Add(parent);

        // Act
        var retrievedParent = TaskRepo.GetById(parent.Id);

        // Assert
        retrievedParent.Should().NotBeNull();
        retrievedParent!.Subtasks.Should().HaveCount(1);
        retrievedParent.Subtasks.First().Title.Should().Be("My Subtask");

        // Subtask should NOT appear in GetAll (it's not a TaskItem)
        TaskRepo.GetAll().Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that completing a parent task does not affect the statuses of its subtasks.
    /// </summary>
    [Fact]
    public void CompleteParentTask_DoesNotAffectSubtaskStatuses()
    {
        // Arrange
        DateTime today = TodoDay.Today.ToDateTime();
        var subtaskPlanned = new SubtaskItemBuilder()
            .WithId(601)
            .WithTitle("Planned Subtask")
            .Build();

        var subtaskCompleted = new SubtaskItemBuilder()
            .WithId(603)
            .WithTitle("Completed Subtask")
            .WithStatus(TaskStatus.Completed)
            .Build();

        var parent = new TaskItemBuilder()
            .WithId(600)
            .WithTitle("Parent Task")
            .WithScheduledDate(today, DateSource.AutoFixed)
            .WithStatus(TaskStatus.Planned)
            .WithSubtask(subtaskPlanned)
            .WithSubtask(subtaskCompleted)
            .Build();

        TaskRepo.Add(parent);

        // Act - complete the parent task
        TaskRepo.CompleteTask(parent.Id);

        // Assert - parent is completed
        var retrievedParent = TaskRepo.GetById(parent.Id);
        retrievedParent.Should().NotBeNull();
        retrievedParent!.Status.Should().Be(TaskStatus.Completed);

        // Assert - subtasks retain their original statuses
        retrievedParent.Subtasks.Should().HaveCount(2);
        retrievedParent.Subtasks.First(s => s.Id == 601).Status.Should().Be(TaskStatus.Planned);
    }

    /// <summary>
    /// Verifies subtask SortOrder is preserved through repository operations.
    /// </summary>
    [Fact]
    public void SubtaskSortOrder_PreservedThroughRepository()
    {
        // Arrange
        var sub1 = new SubtaskItemBuilder().WithId(701).WithTitle("First").WithSortOrder(0).Build();
        var sub2 = new SubtaskItemBuilder().WithId(702).WithTitle("Second").WithSortOrder(1).Build();
        var sub3 = new SubtaskItemBuilder().WithId(703).WithTitle("Third").WithSortOrder(2).Build();

        var parent = new TaskItemBuilder()
            .WithId(700)
            .WithSubtask(sub1)
            .WithSubtask(sub2)
            .WithSubtask(sub3)
            .Build();

        TaskRepo.Add(parent);

        // Act
        var retrieved = TaskRepo.GetById(parent.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Subtasks.OrderBy(s => s.SortOrder).Select(s => s.Title)
            .Should().BeEquivalentTo(["First", "Second", "Third"], o => o.WithStrictOrdering());
    }

    /// <summary>
    /// Verifies toggling a subtask favorite status.
    /// </summary>
    [Fact]
    public void ToggleSubtaskFavorite_TogglesIsFavoriteCorrectly()
    {
        // Arrange
        var sub = new SubtaskItemBuilder().WithId(801).WithTitle("Fav Subtask").WithFavorite(false).Build();
        var parent = new TaskItemBuilder().WithId(800).WithSubtask(sub).Build();
        TaskRepo.Add(parent);

        // Act 1: toggle to true
        TaskRepo.ToggleSubtaskFavorite(sub.Id);

        // Assert 1
        var retrieved1 = TaskRepo.GetById(parent.Id)!.Subtasks.First(s => s.Id == sub.Id);
        retrieved1.IsFavorite.Should().BeTrue();

        // Act 2: toggle back to false
        TaskRepo.ToggleSubtaskFavorite(sub.Id);

        // Assert 2
        var retrieved2 = TaskRepo.GetById(parent.Id)!.Subtasks.First(s => s.Id == sub.Id);
        retrieved2.IsFavorite.Should().BeFalse();
    }
}
