using FluentAssertions;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using NSubstitute;

namespace FlowFocus.Tests;

[Trait("Category", "Domain")]
[Collection("StaticState")]
public class SubtasksEngineTests
{
    public class Aggregation
    {
        [Fact]
        public void CalculateTotalMinutesAndComplexity_AggregatesParentAndSubtasksFromRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context, Substitute.For<INotificationService>());

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
            taskRepo.Add(parent);
            var savedParent = taskRepo.GetById(parent.Id);

            // Assert
            savedParent.Should().NotBeNull();
            savedParent.TotalEstimatedMinutes.Should().Be(90);
            savedParent.TotalComplexity.Should().Be(30);
        }
    }

    public class ListIsolation
    {
        [Fact]
        public void RepositoryRootQuery_ExcludesSubtasksWithNonNullParentId()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context, Substitute.For<INotificationService>());

            var subtask = new TaskItemBuilder().WithId(201).WithTitle("Subtask").WithParentTaskId(200).Build();
            var mainTask = new TaskItemBuilder().WithId(200).WithTitle("Main Parent Task").WithSubtask(subtask).Build();

            taskRepo.Add(mainTask);

            // Act: Query repository root tasks
            var rootTasks = taskRepo.GetAll().Where(t => t.ParentTaskId == null).ToList();

            // Assert
            rootTasks.Should().ContainSingle();
            rootTasks.First().Id.Should().Be(200);
        }
    }

    public class TruncatedEditFields
    {
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
    }

    [Fact]
    public void SubtaskPriority_ExceedingParentPriority_ThrowsValidationError()
    {
        // Arrange: В FlowFocus свойство Order задаёт вес приоритета (меньше значение = выше приоритет).
        // Medium (Order = 3), High (Order = 2 — выше приоритета родителя).
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
        // Act: Вызов валидатора иерархии подзадач
        // TODO: убедиться, что нужна эта валидация в этом методе и в SubtaskDates_MismatchingParent_ThrowsValidationError
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask, subtask);
        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*приоритет не может быть выше приоритета родительской*");
    }

    [Fact]
    public void SubtaskDates_MismatchingParent_ThrowsValidationError()
    {
        // Arrange: В FlowFocus используется единая дата ScheduledDate.
        // Дата подзадачи не должна быть позже или отличаться от даты родительской задачи.
        DateTime parentDate = new(2026, 8, 10);

        var parentTask = new TaskItemBuilder()
            .WithId(20)
            .WithScheduledDate(parentDate)
            .Build();
        var subtask = new TaskItemBuilder()
            .WithId(21)
            .WithScheduledDate(new DateTime(2026, 8, 11)) // Дата позже parentDate
            .WithParentTask(parentTask)
            .Build();
        // Act
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask, subtask);
        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*даты назначения должны совпадать, а дата не может быть позже*");
    }
}