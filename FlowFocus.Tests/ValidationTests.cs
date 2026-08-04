using FluentAssertions;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for TaskItem and TaskRelation domain validation rules and numeric constraints.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Validation")]
public class ValidationTests : IntegrationTestBase
{
    /// <summary>
    /// Verifies that empty, whitespace, or null titles return false.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOrWhitespaceTitle_ReturnsFalse(string? inputTitle)
    {
        // Act
        var isValid = TaskItemValidator.IsTitleValid(inputTitle);

        // Assert
        isValid.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that valid non-empty titles return true.
    /// </summary>
    [Fact]
    public void ValidTitle_ReturnsTrue()
    {
        // Act
        var isValid = TaskItemValidator.IsTitleValid("Купить хлеб");

        // Assert
        isValid.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that attempting to create a relation between a task and itself throws a validation exception.
    /// </summary>
    [Fact]
    public void SelectSelfTaskInRelations_ThrowsValidationError()
    {
        // Arrange
        var task = new TaskItemBuilder().WithId(10).WithTitle("Task A").Build();

        // Act
        var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: task, targetTask: task, type: RelationType.Blocks);

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage(expectedWildcardPattern: "*сама с собой*");
    }

    /// <summary>
    /// Verifies that linking relations to a recurring task throws a validation exception.
    /// </summary>
    [Fact]
    public void SelectRecurringTaskAsRelation_ThrowsValidationError()
    {
        // Arrange
        var taskA = new TaskItemBuilder().WithId(1).WithTitle("Regular Task").Build();
        var taskB = new TaskItemBuilder().WithId(2).WithTitle("Recurring Task").WithRecurrence(type: RecurrenceType.Daily).Build();

        // Act
        var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: taskA, targetTask: taskB, type: RelationType.Blocks);

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage(expectedWildcardPattern: "*повторяющимися задачами запрещены*");
    }

    /// <summary>
    /// Verifies that creating more than 15 relations on a single task throws a validation exception.
    /// </summary>
    [Fact]
    public void Exceeding15Relations_ThrowsValidationError()
    {
        // Arrange
        var taskA = new TaskItemBuilder().WithId(100).WithTitle("Main Task").Build();
        for (var i = 1; i <= 15; i++)
        {
            var target = new TaskItemBuilder().WithId(i).Build();
            taskA.Relations.Add(new() { SourceTaskId = taskA.Id, TargetTaskId = target.Id, Type = RelationType.RelatedTo });
        }

        var extraTask = new TaskItemBuilder().WithId(101).Build();

        // Act
        var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: taskA, targetTask: extraTask, type: RelationType.RelatedTo);

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage(expectedWildcardPattern: "*Достигнут лимит количества связей (15/15)*");
    }

    /// <summary>
    /// Verifies that interest values are clamped to the 1-10 valid range.
    /// </summary>
    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    [InlineData(11, 10)]
    public void InterestValue_IsClampedToValidRange(int input, int expected)
    {
        // Act
        var result = TaskItemValidator.ClampInterest(input);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that complexity values are clamped to the 1-100 valid range.
    /// </summary>
    [Theory]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(50, 50)]
    [InlineData(150, 100)]
    public void ComplexityValue_IsClampedToValidRange(int input, int expected)
    {
        // Act
        var result = TaskItemValidator.ClampComplexity(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void UpdateSubtask_WithForbiddenFields_IgnoresChangesOrThrows()
    {
        // Arrange
        var parentTask = new TaskItemBuilder().WithId(7000).WithScheduledDate(new DateTime(2026, 1, 1)).Build();
        var subtask = new TaskItemBuilder().WithId(7001).WithParentTask(parentTask).Build();

        TaskRepo.Add(parentTask);
        TaskRepo.Add(subtask);
        Context.ChangeTracker.Clear();

        // Act
        var updatedSubtask = TaskRepo.GetById(7001);
        updatedSubtask!.IsRecurring = true;
        updatedSubtask.RecurrenceType = RecurrenceType.Daily;
        updatedSubtask.ScheduledDate = new DateTime(2027, 1, 1);

        var act = () => TaskRepo.Update(updatedSubtask);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*подзадачи не поддерживают независимые даты или повторения*");
    }
}
