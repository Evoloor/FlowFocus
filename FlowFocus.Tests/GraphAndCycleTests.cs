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
[Trait(name: "Category", value: "Graph")]
[Collection(name: "StaticState")]
public class GraphAndCycleTests
{
    /// <summary>
    /// Tests verification of circular blockage graph validation.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Graph")]
    public class CircularBlockages
    {
        /// <summary>
        /// Verifies that direct or indirect circular blockage cycles throw a CircularDependencyException.
        /// </summary>
        [Fact]
        public void DirectOrIndirectCircularBlockage_ThrowsCircularDependencyException()
        {
            // Arrange: A blocks B, B blocks C
            var taskA = new TaskItemBuilder().WithId(id: 1).WithTitle(title: "Task A").Build();
            _ = new TaskItemBuilder().WithId(id: 2).WithTitle(title: "Task B").Build();
            var taskC = new TaskItemBuilder().WithId(id: 3).WithTitle(title: "Task C").Build();

            List<TaskRelation> graph =
            [
                new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks },
                new() { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }
            ];

            // Act: Call real domain validator to add "C blocks A"
            var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: taskC, targetTask: taskA, type: RelationType.Blocks, existingRelationsGraph: graph);

            // Assert
            act.Should().Throw<CircularDependencyException>()
               .WithMessage(expectedWildcardPattern: "*циклический граф*");
        }
    }

    /// <summary>
    /// Tests verification of subtask self-nesting validation.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Graph")]
    public class SubtaskSelfNesting
    {
        /// <summary>
        /// Verifies that making a parent task a subtask of its own subtask throws a validation exception.
        /// </summary>
        [Fact]
        public void MakeParentTaskSubtaskOfItsOwnSubtask_ThrowsValidationError()
        {
            // Arrange: Task A contains Subtask B
            var taskB = new TaskItemBuilder().WithId(id: 20).WithTitle(title: "Child B").Build();
            var taskA = new TaskItemBuilder().WithId(id: 10).WithTitle(title: "Parent A").WithSubtask(subtask: taskB).Build();

            // Act: Call real domain validator to attempt making "A" a subtask of B
            var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask: taskB, childTask: taskA);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage(expectedWildcardPattern: "*Запрещена циклическая вложенность*");
        }
    }

    /// <summary>
    /// Tests verification of stack overflow protection during graph traversal.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Graph")]
    public class StackOverflowProtection
    {
    }
}
