using FluentAssertions;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Trait("Category", "Validation")]
public class ValidationTests
{
    public class TitleValidation
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void EmptyOrWhitespaceTitle_ReturnsFalse(string? inputTitle)
        {
            // Act: Call application validator
            var isValid = TaskItemValidator.IsTitleValid(inputTitle);

            // Assert
            isValid.Should().BeFalse();
        }

        [Fact]
        public void ValidTitle_ReturnsTrue()
        {
            // Act: Call application validator
            var isValid = TaskItemValidator.IsTitleValid("Купить хлеб");

            // Assert
            isValid.Should().BeTrue();
        }
    }

    public class SelfRelation
    {
        [Fact]
        public void SelectSelfTaskInRelations_ThrowsValidationError()
        {
            // Arrange
            var task = new TaskItemBuilder().WithId(10).WithTitle("Task A").Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(task, task, RelationType.Blocks);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*сама с собой*");
        }
    }

    public class RecurringTaskRelations
    {
        [Fact]
        public void SelectRecurringTaskAsRelation_ThrowsValidationError()
        {
            // Arrange
            var taskA = new TaskItemBuilder().WithId(1).WithTitle("Regular Task").Build();
            var taskB = new TaskItemBuilder().WithId(2).WithTitle("Recurring Task").WithRecurrence(RecurrenceType.Daily).Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(taskA, taskB, RelationType.Blocks);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*повторяющимися задачами запрещены*");
        }
    }

    public class RelationsLimit
    {
        [Fact]
        public void Exceeding15Relations_ThrowsValidationError()
        {
            // Arrange
            var taskA = new TaskItemBuilder().WithId(100).WithTitle("Main Task").Build();
            for (var i = 1; i <= 15; i++)
            {
                var target = new TaskItemBuilder().WithId(i).Build();
                taskA.Relations.Add(new Core.Models.TaskRelation { SourceTaskId = taskA.Id, TargetTaskId = target.Id, Type = RelationType.RelatedTo });
            }

            var extraTask = new TaskItemBuilder().WithId(101).Build();

            // Act: Call real domain validator
            var act = () => TaskRelationValidator.ValidateNewRelation(taskA, extraTask, RelationType.RelatedTo);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*Достигнут лимит количества связей (15/15)*");
        }
    }

    public class NumericRanges
    {
        [Theory]
        [InlineData(-5, 1)]
        [InlineData(0, 1)]
        [InlineData(5, 5)]
        [InlineData(11, 10)]
        public void InterestValue_IsClampedToValidRange(int input, int expected)
        {
            // Act: Call real domain validator
            var result = TaskItemValidator.ClampInterest(input);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(-10, 1)]
        [InlineData(0, 1)]
        [InlineData(50, 50)]
        [InlineData(150, 100)]
        public void ComplexityValue_IsClampedToValidRange(int input, int expected)
        {
            // Act: Call real domain validator
            var result = TaskItemValidator.ClampComplexity(input);

            // Assert
            result.Should().Be(expected);
        }
    }
    public class RelationTargetStatusValidation
    {
        [Theory]
        [InlineData(TaskStatus.Completed)]
        [InlineData(TaskStatus.Irrelevant)]
        public void CreateRelationWithInactiveTask_ThrowsValidationError(TaskStatus inactiveStatus)
        {
            // Arrange: Запрет связей с завершенными или неактуальными задачами[cite: 1]
            var activeTask = new TaskItemBuilder().WithId(1).WithStatus(TaskStatus.Planned).Build();
            var inactiveTask = new TaskItemBuilder().WithId(2).WithStatus(inactiveStatus).Build();

            // Act: Попытка добавить связь к неактуальной задаче
            var act = () => TaskRelationValidator.ValidateNewRelation(activeTask, inactiveTask, RelationType.RelatedTo);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*ссылаться можно только на актуальные задачи*");
        }
    }
}
