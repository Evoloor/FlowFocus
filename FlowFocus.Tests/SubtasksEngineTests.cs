using FluentAssertions;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;

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
        var (mainTask, _) = TaskItemBuilder.CreateParentWithSubtasks(1, 200);

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
    /// Verifies that mismatching scheduled dates between subtask and parent throws a validation exception.
    /// </summary>
    [Fact]
    public void SubtaskDates_MismatchingParent_ThrowsValidationError()
    {
        // Arrange
        DateTime parentDate = new(2026, 8, 10);

        var parentTask = new TaskItemBuilder()
            .WithId(20)
            .WithScheduledDate(parentDate)
            .Build();
        var subtask = new TaskItemBuilder()
            .WithId(21)
            .WithScheduledDate(new DateTime(2026, 8, 11))
            .WithParentTask(parentTask)
            .Build();

        // Act
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask: parentTask, childTask: subtask);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedWildcardPattern: "*даты назначения должны совпадать, а дата не может быть позже*");
    }
}