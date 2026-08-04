using FluentAssertions;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for TaskItem and TaskRelation domain validation rules and numeric constraints.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Validation")]
public class ValidationTests
{
    /// <summary>
    /// Tests verification of task title string validations.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Validation")]
    public class TitleValidation
    {
        /// <summary>
        /// Verifies that empty, whitespace, or null titles return false.
        /// </summary>
        [Theory]
        [InlineData(data: "")]
        [InlineData(data: "   ")]
        [InlineData(data: null)]
        public void EmptyOrWhitespaceTitle_ReturnsFalse(string? inputTitle)
        {
            // Act: Call application validator
            var isValid = TaskItemValidator.IsTitleValid(title: inputTitle);

            // Assert
            isValid.Should().BeFalse();
        }

        /// <summary>
        /// Verifies that valid non-empty titles return true.
        /// </summary>
        [Fact]
        public void ValidTitle_ReturnsTrue()
        {
            // Act: Call application validator
            var isValid = TaskItemValidator.IsTitleValid(title: "Купить хлеб");

            // Assert
            isValid.Should().BeTrue();
        }
    }

    /// <summary>
    /// Tests verification of self-referencing relation constraints.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Validation")]
    public class SelfRelation
    {
        /// <summary>
        /// Verifies that attempting to create a relation between a task and itself throws a validation exception.
        /// </summary>
        [Fact]
        public void SelectSelfTaskInRelations_ThrowsValidationError()
        {
            // Arrange
            var task = new TaskItemBuilder().WithId(id: 10).WithTitle(title: "Task A").Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: task, targetTask: task, type: RelationType.Blocks);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage(expectedWildcardPattern: "*сама с собой*");
        }
    }

    /// <summary>
    /// Tests verification of relation rules for recurring tasks.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Validation")]
    public class RecurringTaskRelations
    {
        /// <summary>
        /// Verifies that linking relations to a recurring task throws a validation exception.
        /// </summary>
        [Fact]
        public void SelectRecurringTaskAsRelation_ThrowsValidationError()
        {
            // Arrange
            var taskA = new TaskItemBuilder().WithId(id: 1).WithTitle(title: "Regular Task").Build();
            var taskB = new TaskItemBuilder().WithId(id: 2).WithTitle(title: "Recurring Task").WithRecurrence(type: RecurrenceType.Daily).Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: taskA, targetTask: taskB, type: RelationType.Blocks);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage(expectedWildcardPattern: "*повторяющимися задачами запрещены*");
        }
    }

    /// <summary>
    /// Tests verification of upper limit enforcement on total relations per task.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Validation")]
    public class RelationsLimit
    {
        /// <summary>
        /// Verifies that creating more than 15 relations on a single task throws a validation exception.
        /// </summary>
        [Fact]
        public void Exceeding15Relations_ThrowsValidationError()
        {
            // Arrange
            var taskA = new TaskItemBuilder().WithId(id: 100).WithTitle(title: "Main Task").Build();
            for (var i = 1; i <= 15; i++)
            {
                var target = new TaskItemBuilder().WithId(id: i).Build();
                taskA.Relations.Add(item: new() { SourceTaskId = taskA.Id, TargetTaskId = target.Id, Type = RelationType.RelatedTo });
            }

            var extraTask = new TaskItemBuilder().WithId(id: 101).Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(sourceTask: taskA, targetTask: extraTask, type: RelationType.RelatedTo);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage(expectedWildcardPattern: "*Достигнут лимит количества связей (15/15)*");
        }
    }

    /// <summary>
    /// Tests verification of numeric clamping ranges for task properties.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Validation")]
    public class NumericRanges
    {
        /// <summary>
        /// Verifies that interest values are clamped to the 1-10 valid range.
        /// </summary>
        [Theory]
        [InlineData(data: [-5, 1])]
        [InlineData(data: [0, 1])]
        [InlineData(data: [5, 5])]
        [InlineData(data: [11, 10])]
        public void InterestValue_IsClampedToValidRange(int input, int expected)
        {
            // Act: Call real domain validator
            var result = TaskItemValidator.ClampInterest(interest: input);

            // Assert
            result.Should().Be(expected: expected);
        }

        /// <summary>
        /// Verifies that complexity values are clamped to the 1-100 valid range.
        /// </summary>
        [Theory]
        [InlineData(data: [-10, 1])]
        [InlineData(data: [0, 1])]
        [InlineData(data: [50, 50])]
        [InlineData(data: [150, 100])]
        public void ComplexityValue_IsClampedToValidRange(int input, int expected)
        {
            // Act: Call real domain validator
            var result = TaskItemValidator.ClampComplexity(complexity: input);

            // Assert
            result.Should().Be(expected: expected);
        }
    }
    
    [Fact]
    public void UpdateSubtask_WithForbiddenFields_IgnoresChangesOrThrows()
    {
        // Arrange: К подзадачам не относится функционал помимо базовых полей[cite: 1]
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

        var parentTask = new TaskItemBuilder().WithId(7000).WithScheduledDate(new DateTime(2026, 1, 1)).Build();
        var subtask = new TaskItemBuilder().WithId(7001).WithParentTask(parentTask).Build();
    
        taskRepo.Add(parentTask);
        taskRepo.Add(subtask);
        context.ChangeTracker.Clear();

        // Act: Попытка обновить запрещенные для подзадачи поля (рекурсия, независимая дата)
        var updatedSubtask = taskRepo.GetById(7001);
        updatedSubtask!.IsRecurring = true;
        updatedSubtask.RecurrenceType = RecurrenceType.Daily;
        updatedSubtask.ScheduledDate = new DateTime(2027, 1, 1);
    
        // Вызываем общий метод сохранения
        var act = () => taskRepo.Update(updatedSubtask);

        // Assert: Система должна либо отклонить такую транзакцию, либо проигнорировать запрещенные поля
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*подзадачи не поддерживают независимые даты или повторения*");
    }
}
