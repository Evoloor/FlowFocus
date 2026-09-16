using FluentAssertions;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Exceptions;
using FlowFocus.Core.Models;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for circular blockage graphs, subtask self-nesting cycles, and recursion safety.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Graph")]
[Collection("StaticState")]
public class GraphAndCycleTests
{
    /// <summary>
    /// Verifies that direct or indirect circular blockage cycles throw a CircularDependencyException.
    /// </summary>
    [Fact]
    public void DirectOrIndirectCircularBlockage_ThrowsCircularDependencyException()
    {
        // Arrange
        var taskA = new TaskItemBuilder().WithId(1).WithTitle("Task A").Build();
        _ = new TaskItemBuilder().WithId(2).WithTitle("Task B").Build();
        var taskC = new TaskItemBuilder().WithId(3).WithTitle("Task C").Build();

        List<TaskRelation> graph =
        [
            new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks },
            new() { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }
        ];

        // Act
        var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: taskC, targetTask: taskA, type: RelationType.Blocks, existingRelationsGraph: graph);

        // Assert
        act.Should().Throw<CircularDependencyException>()
           .WithMessage(expectedWildcardPattern: "*циклический граф*");
    }

    /// <summary>
    /// Verifies that validating a subtask with mismatched ParentTaskId throws a validation exception.
    /// </summary>
    [Fact]
    public void Subtask_MismatchedParentTaskId_ThrowsValidationError()
    {
        // Arrange
        var taskA = new TaskItemBuilder().WithId(10).WithTitle("Task A").Build();
        var subtask = new SubtaskItemBuilder().WithId(5).WithTitle("Other Parent Subtask").WithParentTaskId(20).Build();

        // Act
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(taskA, subtask);

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage(expectedWildcardPattern: "*привязана к другой родительской задаче*");
    }

    /// <summary>
    /// Verifies that a task and subtask can share the same numeric ID without conflict.
    /// </summary>
    [Fact]
    public void Subtask_WithSameNumericIdAsParent_DoesNotThrow()
    {
        // Arrange
        var taskA = new TaskItemBuilder().WithId(10).WithTitle("Task A").Build();
        var subtask = new SubtaskItemBuilder().WithId(10).WithTitle("Same ID Subtask").WithParentTaskId(10).Build();

        // Act
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(taskA, subtask);

        // Assert
        act.Should().NotThrow();
    }
}
