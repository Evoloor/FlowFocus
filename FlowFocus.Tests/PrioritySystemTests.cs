using FluentAssertions;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;

namespace FlowFocus.Tests;

/// <summary>
/// Domain unit tests for priority system behavior, default configurations, reordering, and boundary limits.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Domain")]
[Collection(name: "StaticState")]
public class PrioritySystemTests
{
    /// <summary>
    /// Tests verification of default system priority level configuration.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class DefaultConfiguration
    {
        /// <summary>
        /// Verifies that loading default configuration yields strictly 5 base priorities in order.
        /// </summary>
        [Fact]
        public void LoadDefaultConfiguration_ReturnsStrictlyFiveBasePriorities()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            PriorityRepository repository = new(context: context, notificationService: Substitute.For<INotificationService>());

            // Act
            var priorities = repository.GetAllOrdered();

            // Assert
            priorities.Should().HaveCount(expected: 5);
            priorities.Select(selector: p => p.Name).Should().ContainInConsecutiveOrder(expected: ["Критический", "Высокий", "Средний", "Низкий", "Фоновый"]);
            priorities.Select(selector: p => p.Color).Should().ContainInConsecutiveOrder(expected: ["#FF4444", "#FF8C00", "#FFD700", "#4CAF50", "#2196F3"]);
        }
    }

    /// <summary>
    /// Tests verification of updating custom priority properties.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class CustomSettings
    {
        /// <summary>
        /// Verifies that mutating priority name and color persists correctly in repository.
        /// </summary>
        [Fact]
        public void UpdatePriorityProperties_ReturnsUpdatedNameAndColorFromRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            PriorityRepository repository = new(context: context, notificationService: Substitute.For<INotificationService>());
            var priority = repository.GetAllOrdered().First();

            PriorityLevel updatedPriority = new()
            {
                Id = priority.Id,
                Order = priority.Order,
                Name = "Срочный блокер",
                Color = "#FF0055"
            };

            // Act
            repository.Update(entity: updatedPriority);
            var result = repository.GetById(id: priority.Id);

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be(expected: "Срочный блокер");
            result.Color.Should().Be(expected: "#FF0055");
        }
    }

    /// <summary>
    /// Tests verification of drag-and-drop priority reordering.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class Reordering
    {
        /// <summary>
        /// Verifies that reordering priorities recalculates order indices appropriately.
        /// </summary>
        [Fact]
        public void DragAndDropReorder_RecalculatesOrderIndicesAndComparisons()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            PriorityRepository repository = new(context: context, notificationService: Substitute.For<INotificationService>());
            var initialPriorities = repository.GetAllOrdered();

            List<int> reorderedIds =
            [
                initialPriorities[index: 4].Id, // Move Background (5) to first position
                initialPriorities[index: 0].Id,
                initialPriorities[index: 1].Id,
                initialPriorities[index: 2].Id,
                initialPriorities[index: 3].Id
            ];

            // Act
            repository.Reorder(orderedIds: reorderedIds);
            var result = repository.GetAllOrdered();

            // Assert
            result.First().Id.Should().Be(expected: initialPriorities[index: 4].Id);
            result.First().Order.Should().Be(expected: 1);
        }
    }

    /// <summary>
    /// Tests verification of upper boundary limit enforcement for priorities.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class BoundaryLimits
    {
        /// <summary>
        /// Verifies that adding more than 20 priorities throws a validation exception.
        /// </summary>
        [Fact]
        public void AddingMoreThan20Priorities_BlocksOperationAtLimit()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            PriorityRepository repository = new(context: context, notificationService: Substitute.For<INotificationService>());

            for (var i = 6; i <= 20; i++)
            {
                repository.Add(entity: new PriorityLevelBuilder().WithId(id: i).WithOrder(order: i).WithName(name: $"Priority {i}").Build());
            }

            // Act: Attempt to add 21st priority
            var act = () => repository.Add(entity: new PriorityLevelBuilder().WithId(id: 21).WithOrder(order: 21).WithName(name: "Priority 21").Build());

            // Assert: System must block 21st priority addition
            act.Should().Throw<InvalidOperationException>()
               .WithMessage(expectedWildcardPattern: "*20*");
        }
    }

    /// <summary>
    /// Tests verification of assigning default task priorities from user settings.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class DefaultTaskPriority
    {
        /// <summary>
        /// Verifies that creating a new task assigns default priority ID from user settings.
        /// </summary>
        [Fact]
        public void CreateNewTask_AssignsDefaultPriorityFromUserSettings()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            var userSettings = new UserSettingsBuilder().WithDefaultPriorityId(priorityId: 2).Build();

            var newTask = new TaskItemBuilder()
                .WithId(id: 101)
                .WithTitle(title: "New Task")
                .WithPriorityId(priorityId: userSettings.DefaultPriorityId)
                .Build();

            // Act
            taskRepo.Add(entity: newTask);
            var savedTask = taskRepo.GetById(id: newTask.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.PriorityId.Should().Be(expected: 2);
        }
    }
}
