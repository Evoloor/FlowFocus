using FluentAssertions;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for subtask aggregation, list isolation, edit field truncation, and hierarchy validation.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Domain")]
[Collection(name: "StaticState")]
public class SubtasksEngineTests
{
    /// <summary>
    /// Tests verification of subtask duration and complexity aggregation.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class Aggregation
    {
        /// <summary>
        /// Verifies that total estimated minutes and complexity aggregate parent and subtasks recursively.
        /// </summary>
        [Fact]
        public void CalculateTotalMinutesAndComplexity_AggregatesParentAndSubtasksFromRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            var subtask1 = new TaskItemBuilder().WithId(id: 101).WithEstimatedMinutes(minutes: 15).WithComplexity(complexity: 5).Build();
            var subtask2 = new TaskItemBuilder().WithId(id: 102).WithEstimatedMinutes(minutes: 45).WithComplexity(complexity: 15).Build();

            var parent = new TaskItemBuilder()
                .WithId(id: 100)
                .WithEstimatedMinutes(minutes: 30)
                .WithComplexity(complexity: 10)
                .WithSubtask(subtask: subtask1)
                .WithSubtask(subtask: subtask2)
                .Build();

            // Act
            taskRepo.Add(entity: parent);
            var savedParent = taskRepo.GetById(id: parent.Id);

            // Assert
            savedParent.Should().NotBeNull();
            savedParent.TotalEstimatedMinutes.Should().Be(expected: 90);
            savedParent.TotalComplexity.Should().Be(expected: 30);
        }
    }

    /// <summary>
    /// Tests verification of root task query isolation from subtasks.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class ListIsolation
    {
        /// <summary>
        /// Verifies that repository root queries exclude subtasks with non-null ParentTaskId.
        /// </summary>
        [Fact]
        public void RepositoryRootQuery_ExcludesSubtasksWithNonNullParentId()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            var subtask = new TaskItemBuilder().WithId(id: 201).WithTitle(title: "Subtask").WithParentTaskId(parentId: 200).Build();
            var mainTask = new TaskItemBuilder().WithId(id: 200).WithTitle(title: "Main Parent Task").WithSubtask(subtask: subtask).Build();

            taskRepo.Add(entity: mainTask);

            // Act: Query repository root tasks
            var rootTasks = taskRepo.GetAll().Where(predicate: t => t.ParentTaskId == null).ToList();

            // Assert
            rootTasks.Should().ContainSingle();
            rootTasks.First().Id.Should().Be(expected: 200);
        }
    }

    /// <summary>
    /// Tests verification of field constraints on subtask models.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class TruncatedEditFields
    {
        /// <summary>
        /// Verifies that subtask models expose only valid allowed subtask properties.
        /// </summary>
        [Fact]
        public void SubtaskModel_ExposesOnlyAllowedSubtaskFields()
        {
            // Arrange & Act
            var subtask = new TaskItemBuilder()
                .WithTitle(title: "Subtask Title")
                .WithInterest(interest: 8)
                .WithComplexity(complexity: 20)
                .WithEstimatedMinutes(minutes: 25)
                .WithParentTaskId(parentId: 400)
                .Build();

            // Assert
            subtask.IsSubtask.Should().BeTrue();
            subtask.Title.Should().Be(expected: "Subtask Title");
            subtask.Interest.Should().Be(expected: 8);
            subtask.Complexity.Should().Be(expected: 20);
            subtask.EstimatedMinutes.Should().Be(expected: 25);
            subtask.ParentTaskId.Should().Be(expected: 400);
            subtask.IsRecurring.Should().BeFalse();
            subtask.ScheduledDate.Should().BeNull();
        }
    }

    /// <summary>
    /// Verifies that assigning a higher priority to a subtask than its parent throws a validation exception.
    /// </summary>
    [Fact]
    public void SubtaskPriority_ExceedingParentPriority_ThrowsValidationError()
    {
        // Arrange: Priority order (lower value = higher priority). High (Order 2) > Medium (Order 3).
        var parentPriority = PriorityLevelBuilder.Medium;
        var subtaskPriority = PriorityLevelBuilder.High;
        var parentTask = new TaskItemBuilder()
            .WithId(id: 10)
            .WithPriority(priority: parentPriority)
            .Build();
        var subtask = new TaskItemBuilder()
            .WithId(id: 11)
            .WithPriority(priority: subtaskPriority)
            .WithParentTask(parentTask: parentTask)
            .Build();

        // Act: Validate subtask hierarchy
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
        // Arrange: Subtask date must not differ from parent scheduled date
        DateTime parentDate = new(year: 2026, month: 8, day: 10);

        var parentTask = new TaskItemBuilder()
            .WithId(id: 20)
            .WithScheduledDate(date: parentDate)
            .Build();
        var subtask = new TaskItemBuilder()
            .WithId(id: 21)
            .WithScheduledDate(date: new DateTime(year: 2026, month: 8, day: 11))
            .WithParentTask(parentTask: parentTask)
            .Build();

        // Act: Validate subtask hierarchy
        var act = () => TaskHierarchyValidator.ValidateSubtaskParent(parentTask: parentTask, childTask: subtask);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedWildcardPattern: "*даты назначения должны совпадать, а дата не может быть позже*");
    }
}