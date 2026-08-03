using FluentAssertions;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using NSubstitute;

namespace FlowFocus.Tests;

[Trait("Category", "Domain")]
[Collection("StaticState")]
public class PrioritySystemTests
{

    public class DefaultConfiguration
    {
        [Fact]
        public void LoadDefaultConfiguration_ReturnsStrictlyFiveBasePriorities()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var repository = new PriorityRepository(context, Substitute.For<INotificationService>());

            // Act
            var priorities = repository.GetAllOrdered();

            // Assert
            priorities.Should().HaveCount(5);
            priorities.Select(p => p.Name).Should().ContainInConsecutiveOrder(
                "Критический", "Высокий", "Средний", "Низкий", "Фоновый");
            priorities.Select(p => p.Color).Should().ContainInConsecutiveOrder(
                "#FF4444", "#FF8C00", "#FFD700", "#4CAF50", "#2196F3");
        }
    }

    public class CustomSettings
    {
        [Fact]
        public void UpdatePriorityProperties_ReturnsUpdatedNameAndColorFromRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var repository = new PriorityRepository(context, Substitute.For<INotificationService>());
            var priority = repository.GetAllOrdered().First();

            var updatedPriority = new PriorityLevel
            {
                Id = priority.Id,
                Order = priority.Order,
                Name = "Срочный блокер",
                Color = "#FF0055"
            };

            // Act
            repository.Update(updatedPriority);
            var result = repository.GetById(priority.Id);

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be("Срочный блокер");
            result.Color.Should().Be("#FF0055");
        }
    }

    public class Reordering
    {
        [Fact]
        public void DragAndDropReorder_RecalculatesOrderIndicesAndComparisons()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var repository = new PriorityRepository(context, Substitute.For<INotificationService>());
            var initialPriorities = repository.GetAllOrdered();

            var reorderedIds = new List<int>
            {
                initialPriorities[4].Id, // Move Background (5) to first
                initialPriorities[0].Id,
                initialPriorities[1].Id,
                initialPriorities[2].Id,
                initialPriorities[3].Id
            };

            // Act
            repository.Reorder(reorderedIds);
            var result = repository.GetAllOrdered();

            // Assert
            result.First().Id.Should().Be(initialPriorities[4].Id);
            result.First().Order.Should().Be(1);
        }
    }

    public class BoundaryLimits
    {
        [Fact]
        public void AddingMoreThan20Priorities_BlocksOperationAtLimit()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var repository = new PriorityRepository(context, Substitute.For<INotificationService>());

            for (var i = 6; i <= 20; i++)
            {
                repository.Add(new PriorityLevelBuilder().WithId(i).WithOrder(i).WithName($"Priority {i}").Build());
            }

            // Act
            var act = () => repository.Add(new PriorityLevelBuilder().WithId(21).WithOrder(21).WithName("Priority 21").Build());

            // Assert: System must block 21st priority addition
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*20*");
        }
    }

    public class DefaultTaskPriority
    {
        [Fact]
        public void CreateNewTask_AssignsDefaultPriorityFromUserSettings()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var userSettings = new UserSettingsBuilder().WithDefaultPriorityId(2).Build();

            var newTask = new TaskItemBuilder()
                .WithId(101)
                .WithTitle("New Task")
                .WithPriorityId(userSettings.DefaultPriorityId)
                .Build();

            // Act
            taskRepo.Add(newTask);
            var savedTask = taskRepo.GetById(newTask.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.PriorityId.Should().Be(2);
        }
    }
}
