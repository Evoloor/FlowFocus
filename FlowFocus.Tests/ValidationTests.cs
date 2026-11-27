using FluentAssertions;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
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
        var taskB = new TaskItemBuilder().WithId(2).WithTitle("Recurring Task").WithRecurrence(type: RecurrenceType.EveryN).Build();

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

    /// <summary>
    /// SubtaskItem enforces field restrictions at compile time — it doesn't have
    /// IsRecurring, ScheduledDate, DateSource, PriorityId etc.
    /// This test verifies SubtaskItem is a distinct type from TaskItem.
    /// </summary>
    [Fact]
    public void SubtaskItem_IsNotAssignableToTaskItem()
    {
        // Arrange
        var subtask = new SubtaskItemBuilder().WithId(7001).WithTitle("Test Subtask").Build();

        // Assert — type safety: SubtaskItem is not a TaskItem
        subtask.Should().BeOfType<SubtaskItem>();
        subtask.Should().NotBeAssignableTo<TaskItem>();
    }

    /// <summary>
    /// Verifies that inactive (Completed or Irrelevant) blockers do not fail date/priority relation validation.
    /// </summary>
    [Theory]
    [InlineData(FlowFocus.Core.Enums.TaskStatus.Completed)]
    [InlineData(FlowFocus.Core.Enums.TaskStatus.Irrelevant)]
    public void ValidateRelations_WithInactiveBlocker_IgnoresDateAndPriorityConflicts(FlowFocus.Core.Enums.TaskStatus blockerStatus)
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var critical = priorities[0];
        var low = priorities[^1];

        var blocker = new TaskItemBuilder()
            .WithId(7010)
            .WithTitle("Done Blocker")
            .WithStatus(blockerStatus)
            .WithPriorityId(low.Id)
            .WithScheduledDate(DateTime.Today.AddDays(5))
            .Build();

        var task = new TaskItemBuilder()
            .WithId(7011)
            .WithTitle("Task")
            .WithStatus(FlowFocus.Core.Enums.TaskStatus.Planned)
            .WithPriorityId(critical.Id)
            .WithScheduledDate(DateTime.Today)
            .Build();

        var relations = new List<FlowFocus.Blazor.EditDialogContents.RelationDto>
        {
            new() { Id = 1, Type = RelationType.BlockedBy, TargetTask = blocker }
        };

        // Act
        var result = FlowFocus.Blazor.EditDialogContents.Validators.RelationValidator.ValidateRelations(relations, task, priorities);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateRelations_WhenRaisingBlockedTaskPriorityAboveActiveBlocker_AllowsSave()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var critical = priorities[0];
        var low = priorities[^1];

        var blocker = new TaskItemBuilder()
            .WithId(7020)
            .WithTitle("Active Low Blocker")
            .WithStatus(FlowFocus.Core.Enums.TaskStatus.Planned)
            .WithPriorityId(low.Id)
            .Build();

        var blockedTask = new TaskItemBuilder()
            .WithId(7021)
            .WithTitle("Blocked Critical Task")
            .WithStatus(FlowFocus.Core.Enums.TaskStatus.Blocked)
            .WithPriorityId(critical.Id)
            .Build();

        var relations = new List<FlowFocus.Blazor.EditDialogContents.RelationDto>
        {
            new() { Id = 1, Type = RelationType.BlockedBy, TargetTask = blocker }
        };

        // Act
        var result = FlowFocus.Blazor.EditDialogContents.Validators.RelationValidator.ValidateRelations(relations, blockedTask, priorities);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateRelations_WhenLoweringBlockerPriorityBelowActiveBlocked_ReturnsError()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var critical = priorities[0];
        var low = priorities[^1];

        var blocker = new TaskItemBuilder()
            .WithId(7022)
            .WithTitle("Low Blocker")
            .WithStatus(FlowFocus.Core.Enums.TaskStatus.Planned)
            .WithPriorityId(low.Id)
            .Build();

        var blockedTask = new TaskItemBuilder()
            .WithId(7023)
            .WithTitle("Critical Blocked")
            .WithStatus(FlowFocus.Core.Enums.TaskStatus.Blocked)
            .WithPriorityId(critical.Id)
            .Build();

        var relations = new List<FlowFocus.Blazor.EditDialogContents.RelationDto>
        {
            new() { Id = 1, Type = RelationType.Blocks, TargetTask = blockedTask }
        };

        // Act
        var result = FlowFocus.Blazor.EditDialogContents.Validators.RelationValidator.ValidateRelations(relations, blocker, priorities);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(FlowFocus.Core.Enums.TaskStatus.Completed)]
    [InlineData(FlowFocus.Core.Enums.TaskStatus.Irrelevant)]
    [InlineData(FlowFocus.Core.Enums.TaskStatus.NotConfigured)]
    public void ValidateRelations_IgnoresInactiveTargetInBlocksDirection(FlowFocus.Core.Enums.TaskStatus targetStatus)
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var critical = priorities[0];
        var low = priorities[^1];

        var blocker = new TaskItemBuilder()
            .WithId(7024)
            .WithTitle("Low Blocker")
            .WithStatus(FlowFocus.Core.Enums.TaskStatus.Planned)
            .WithPriorityId(low.Id)
            .WithScheduledDate(DateTime.Today.AddDays(5))
            .Build();

        var inactiveTarget = new TaskItemBuilder()
            .WithId(7025)
            .WithTitle("Inactive Target")
            .WithStatus(targetStatus)
            .WithPriorityId(critical.Id)
            .WithScheduledDate(DateTime.Today)
            .Build();

        var relations = new List<FlowFocus.Blazor.EditDialogContents.RelationDto>
        {
            new() { Id = 1, Type = RelationType.Blocks, TargetTask = inactiveTarget }
        };

        // Act
        var result = FlowFocus.Blazor.EditDialogContents.Validators.RelationValidator.ValidateRelations(relations, blocker, priorities);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
