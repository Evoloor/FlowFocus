using FluentAssertions;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlowFocus.Tests;

[Trait("Category", "Domain")]
public class SubtasksEngineTests
{
    private static StorageContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new StorageContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public class Aggregation
    {
        [Fact]
        public void CalculateTotalMinutesAndComplexity_AggregatesParentAndSubtasksFromRepository()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

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
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

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
}
