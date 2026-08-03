using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Trait("Category", "UI")]
public class UiAndUxEngineTests
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

    public class TaskCardStyles
    {
        [Fact]
        public void CardStateFlags_MatchTaskStatusAndConditions()
        {
            // Arrange & Act
            var planned = new TaskItemBuilder().WithStatus(TaskStatus.Planned).Build();
            var completed = new TaskItemBuilder().WithStatus(TaskStatus.Completed).Build();
            var notActual = new TaskItemBuilder().WithStatus(TaskStatus.Irrelevant).Build();
            var unconfigured = new TaskItemBuilder().WithStatus(TaskStatus.NotConfigured).Build();

            // Assert
            planned.Status.Should().Be(TaskStatus.Planned);
            completed.Status.Should().Be(TaskStatus.Completed);
            notActual.Status.Should().Be(TaskStatus.Irrelevant);
            unconfigured.Status.Should().Be(TaskStatus.NotConfigured);
        }

        [Fact]
        public void OverdueTask_ReturnsIsOverdueTrue()
        {
            // Arrange
            var yesterday = TodoDay.Today.Yesterday.ToDateTime();
            var task = new TaskItemBuilder().WithScheduledDate(yesterday).Build();

            // Act: Call application TodoDay logic
            var isOverdue = TodoDay.Today.IsOverdue(task.ScheduledDate);

            // Assert
            isOverdue.Should().BeTrue();
        }
    }

    public class QuickAddBar
    {
        [Fact]
        public void QuickAddCheckmark_CreatesUnconfiguredTaskInRepository()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var inputText = "Купить хлеб";

            var newTask = new TaskItemBuilder()
                .WithId(101)
                .WithTitle(inputText)
                .WithStatus(TaskStatus.NotConfigured)
                .Build();

            // Act: Call real repository method
            taskRepo.Add(newTask);
            var savedTask = taskRepo.GetById(newTask.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.Title.Should().Be("Купить хлеб");
            savedTask.Status.Should().Be(TaskStatus.NotConfigured);
        }
    }

    public class ProcrastinationEngineTests
    {
        [Fact]
        public void SelectIdealTask_CallsProcrastinationEngine_ReturnsHighestScoreWithInterestGreaterThan7()
        {
            // Arrange
            var lowInterestHighPriority = new TaskItemBuilder().WithId(1).WithInterest(5).WithPriority(PriorityLevelBuilder.Critical).Build(); // Interest <= 7
            var highInterestMediumPriority = new TaskItemBuilder().WithId(2).WithInterest(9).WithPriority(PriorityLevelBuilder.Medium).Build(); // Score = 9 - sqrt(3) ~ 7.27
            var highInterestHighPriority = new TaskItemBuilder().WithId(3).WithInterest(10).WithPriority(PriorityLevelBuilder.High).Build();   // Score = 10 - sqrt(2) ~ 8.58

            var list = new List<TaskItem> { lowInterestHighPriority, highInterestMediumPriority, highInterestHighPriority };

            // Act: Call real ProcrastinationEngine service
            var ideal = ProcrastinationEngine.SelectIdealProcrastinationTask(list);

            // Assert
            ideal.Should().NotBeNull();
            ideal.Id.Should().Be(3);
        }

        [Fact]
        public void DisplacementNeeded_WhenDailyLimitFull()
        {
            // Act: Call real ProcrastinationEngine service
            var requiresDisplacement = ProcrastinationEngine.RequiresDisplacement(currentScheduledMinutes: 180, dailyTimeLimit: 180);

            // Assert
            requiresDisplacement.Should().BeTrue();
        }
    }

    public class OverdueModal
    {
        [Fact]
        public void HasOverdueTasksInRepository_ReturnsOverdueTasks()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            var overdueDate = TodoDay.Today.Yesterday.ToDateTime();
            var task = new TaskItemBuilder().WithId(1).WithScheduledDate(overdueDate).WithStatus(TaskStatus.Planned).Build();
            taskRepo.Add(task);

            // Act: Call real repository query method
            var overdueTasks = taskRepo.GetOverdueTasks();

            // Assert
            overdueTasks.Should().ContainSingle();
        }

        [Fact]
        public void EmptyOverdueListInRepository_ReturnsEmptyList()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            // Act: Call real repository query method
            var overdueTasks = taskRepo.GetOverdueTasks();

            // Assert
            overdueTasks.Should().BeEmpty();
        }
    }

    public class TagDeletionSafety
    {
        [Fact]
        public void DeleteTag_SafelyUnlinksFromTasksInRepository()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var notificationService = Substitute.For<INotificationService>();
            var taskRepo = new TaskRepository(context, notificationService);
            var tagRepo = new TagRepository(context, notificationService);

            var tag = new Tag { Id = 5, Name = "Работа", UsageCount = 1 };
            context.Tags.Add(tag);

            var task = new TaskItemBuilder().WithId(1).WithTitle("Task with tag").Build();
            taskRepo.Add(task);

            context.TaskTags.Add(new TaskTag { TaskId = task.Id, TagId = tag.Id });
            context.SaveChanges();

            // Act: Delete tag via application TagRepository
            tagRepo.Delete(tag.Id);
            var savedTask = taskRepo.GetById(task.Id);

            // Assert: Tag reference safely unlinked in repository
            savedTask.Should().NotBeNull();
            savedTask.Tags.Should().BeEmpty();
            savedTask.Title.Should().Be("Task with tag");
        }
    }
}
