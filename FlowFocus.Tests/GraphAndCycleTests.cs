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
    /// Verifies that making a parent task a subtask of its own subtask throws a validation exception.
    /// </summary>
    [Fact]
    public void MakeParentTaskSubtaskOfItsOwnSubtask_ThrowsValidationError()
    {
        // Arrange
        var taskB = new TaskItemBuilder().WithId(20).WithTitle("Child B").Build();
        var taskA = new TaskItemBuilder().WithId(10).WithTitle("Parent A").WithSubtask(taskB).Build();

        // Act
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask: taskB, childTask: taskA);

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage(expectedWildcardPattern: "*Запрещена циклическая вложенность*");
    }
}
