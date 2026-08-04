using FluentAssertions;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Exceptions;
using FlowFocus.Core.Models;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;

namespace FlowFocus.Tests;

[Trait("Category", "Graph")]
[Collection("StaticState")]
public class GraphAndCycleTests
{

    public class CircularBlockages
    {
        [Fact]
        public void DirectOrIndirectCircularBlockage_ThrowsCircularDependencyException()
        {
            // Arrange: A blocks B, B blocks C
            var taskA = new TaskItemBuilder().WithId(1).WithTitle("Task A").Build();
            var taskB = new TaskItemBuilder().WithId(2).WithTitle("Task B").Build();
            var taskC = new TaskItemBuilder().WithId(3).WithTitle("Task C").Build();

            List<TaskRelation> graph =
            [
                new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks },
                new() { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }
            ];

            // Act: Call real domain validator to add "C blocks A"
            var act = () => TaskRelationValidator.ValidateNewRelation(taskC, taskA, RelationType.Blocks, graph);

            // Assert
            act.Should().Throw<CircularDependencyException>()
               .WithMessage("*циклический граф*");
        }
    }

    public class SubtaskSelfNesting
    {
        [Fact]
        public void MakeParentTaskSubtaskOfItsOwnSubtask_ThrowsValidationError()
        {
            // Arrange: Task A contains Subtask B
            var taskB = new TaskItemBuilder().WithId(20).WithTitle("Child B").Build();
            var taskA = new TaskItemBuilder().WithId(10).WithTitle("Parent A").WithSubtask(taskB).Build();

            // Act: Call real domain validator to attempt making A a subtask of B
            var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask: taskB, childTask: taskA);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*Запрещена циклическая вложенность*");
        }
    }

    public class StackOverflowProtection
    {
        /*[Fact]
        public void CyclicGraphInDb_NormalizeBlockerPriorities_SafelyTerminatesWithoutStackOverflow()
        {
            // Arrange: Artificially create cyclic relation in DB (A -> B -> C -> A)
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskA = new TaskItem { Id = 1, Title = "Task A", Status = TaskStatus.Planned };
            var taskB = new TaskItem { Id = 2, Title = "Task B", Status = TaskStatus.Planned };
            var taskC = new TaskItem { Id = 3, Title = "Task C", Status = TaskStatus.Planned };
            context.Tasks.AddRange(taskA, taskB, taskC);

            context.TaskRelations.Add(new TaskRelation { Id = 100, SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks });
            context.TaskRelations.Add(new TaskRelation { Id = 101, SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks });
            context.TaskRelations.Add(new TaskRelation { Id = 102, SourceTaskId = 3, TargetTaskId = 1, Type = RelationType.Blocks });
            context.SaveChanges();

            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            // Act: Call application service method over cyclic graph
            var act = () => plannerService.NormalizeBlockerPriorities();

            // Assert: Graph traversal algorithm contains visited set and terminates safely without StackOverflowException
            act.Should().NotThrow<StackOverflowException>();
        }*/
    }
}
