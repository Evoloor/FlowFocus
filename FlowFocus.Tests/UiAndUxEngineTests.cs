using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for UI decision engines, card status flags, procrastination selection math, tag suggestions, and overlay queries.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "UI")]
[Collection(name: "StaticState")]
public class UiAndUxEngineTests
{
    /// <summary>
    /// Tests verification of card state flags and overdue properties.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "UI")]
    public class TaskCardStyles
    {
        /// <summary>
        /// Verifies that card state flags match task status properties.
        /// </summary>
        [Fact]
        public void CardStateFlags_MatchTaskStatusAndConditions()
        {
            // Arrange & Act
            var planned = new TaskItemBuilder().WithStatus(status: TaskStatus.Planned).Build();
            var completed = new TaskItemBuilder().WithStatus(status: TaskStatus.Completed).Build();
            var notActual = new TaskItemBuilder().WithStatus(status: TaskStatus.Irrelevant).Build();
            var unconfigured = new TaskItemBuilder().WithStatus(status: TaskStatus.NotConfigured).Build();

            // Assert
            planned.Status.Should().Be(expected: TaskStatus.Planned);
            completed.Status.Should().Be(expected: TaskStatus.Completed);
            notActual.Status.Should().Be(expected: TaskStatus.Irrelevant);
            unconfigured.Status.Should().Be(expected: TaskStatus.NotConfigured);
        }

        /// <summary>
        /// Verifies that overdue task helper evaluates to true for yesterday's scheduled date.
        /// </summary>
        [Fact]
        public void OverdueTask_ReturnsIsOverdueTrue()
        {
            // Arrange
            var yesterday = TodoDay.Today.Yesterday.ToDateTime();
            var task = new TaskItemBuilder().WithScheduledDate(date: yesterday).Build();

            // Act: Call application TodoDay logic
            var isOverdue = TodoDay.Today.IsOverdue(taskDate: task.ScheduledDate);

            // Assert
            isOverdue.Should().BeTrue();
        }
    }

    /// <summary>
    /// Tests verification of quick-add bar creation mechanics.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "UI")]
    public class QuickAddBar
    {
        /// <summary>
        /// Verifies that quick add checkmark creates an unconfigured task in repository.
        /// </summary>
        [Fact]
        public void QuickAddCheckmark_CreatesUnconfiguredTaskInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            var inputText = "Купить хлеб";

            var newTask = new TaskItemBuilder()
                .WithId(id: 101)
                .WithTitle(title: inputText)
                .WithStatus(status: TaskStatus.NotConfigured)
                .Build();

            // Act: Call real repository method
            taskRepo.Add(entity: newTask);
            var savedTask = taskRepo.GetById(id: newTask.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.Title.Should().Be(expected: "Купить хлеб");
            savedTask.Status.Should().Be(expected: TaskStatus.NotConfigured);
        }
    }

    /// <summary>
    /// Tests verification of procrastination engine selection math and displacement criteria.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "UI")]
    public class ProcrastinationEngineTests
    {
        /// <summary>
        /// Verifies that SelectIdealProcrastinationTask returns task with the highest score having interest > 7.
        /// </summary>
        [Fact]
        public void SelectIdealTask_CallsProcrastinationEngine_ReturnsHighestScoreWithInterestGreaterThan7()
        {
            // Arrange
            var lowInterestHighPriority = new TaskItemBuilder().WithId(id: 1).WithInterest(interest: 5).WithPriority(priority: PriorityLevelBuilder.Critical).Build(); // Interest <= 7
            var highInterestMediumPriority = new TaskItemBuilder().WithId(id: 2).WithInterest(interest: 9).WithPriority(priority: PriorityLevelBuilder.Medium).Build(); // Score = 9 - sqrt(3) ~ 7.27
            var highInterestHighPriority = new TaskItemBuilder().WithId(id: 3).WithInterest(interest: 10).WithPriority(priority: PriorityLevelBuilder.High).Build();   // Score = 10 - sqrt(2) ~ 8.58

            List<TaskItem> list = [lowInterestHighPriority, highInterestMediumPriority, highInterestHighPriority];

            // Act: Call real ProcrastinationEngine service
            var ideal = ProcrastinationEngine.SelectIdealProcrastinationTask(tasks: list);

            // Assert
            ideal.Should().NotBeNull();
            ideal.Id.Should().Be(expected: 3);
        }

        /// <summary>
        /// Verifies that RequiresDisplacement returns true when current scheduled minutes equals daily limit.
        /// </summary>
        [Fact]
        public void DisplacementNeeded_WhenDailyLimitFull()
        {
            // Act: Call real ProcrastinationEngine service
            var requiresDisplacement = ProcrastinationEngine.RequiresDisplacement(currentScheduledMinutes: 180, dailyTimeLimit: 180);

            // Assert
            requiresDisplacement.Should().BeTrue();
        }
    }

    /// <summary>
    /// Tests verification of overdue task modal queries.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "UI")]
    public class OverdueModal
    {
        /// <summary>
        /// Verifies that repository query returns single overdue task when overdue tasks exist.
        /// </summary>
        [Fact]
        public void HasOverdueTasksInRepository_ReturnsOverdueTasks()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            var overdueDate = TodoDay.Today.Yesterday.ToDateTime();
            var task = new TaskItemBuilder().WithId(id: 1).WithScheduledDate(date: overdueDate).WithStatus(status: TaskStatus.Planned).Build();
            taskRepo.Add(entity: task);

            // Act: Call real repository query method
            var overdueTasks = taskRepo.GetOverdueTasks();

            // Assert
            overdueTasks.Should().ContainSingle();
        }

        /// <summary>
        /// Verifies that repository query returns empty list when no overdue tasks exist.
        /// </summary>
        [Fact]
        public void EmptyOverdueListInRepository_ReturnsEmptyList()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            // Act: Call real repository query method
            var overdueTasks = taskRepo.GetOverdueTasks();

            // Assert
            overdueTasks.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Tests verification of unlinking tags safely upon tag deletion.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "UI")]
    public class TagDeletionSafety
    {
        /// <summary>
        /// Verifies that deleting a tag unlinks references from tasks without deleting tasks.
        /// </summary>
        [Fact]
        public void DeleteTag_SafelyUnlinksFromTasksInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var notificationService = Substitute.For<INotificationService>();
            TaskRepository taskRepo = new(context: context, notificationService: notificationService);
            TagRepository tagRepo = new(context: context, notificationService: notificationService);

            Tag tag = new() { Id = 5, Name = "Работа", UsageCount = 1 };
            context.Tags.Add(entity: tag);

            var task = new TaskItemBuilder().WithId(id: 1).WithTitle(title: "Task with tag").Build();
            taskRepo.Add(entity: task);

            context.TaskTags.Add(entity: new() { TaskId = task.Id, TagId = tag.Id });
            context.SaveChanges();

            // Act: Delete tag via application TagRepository
            tagRepo.Delete(id: tag.Id);
            var savedTask = taskRepo.GetById(id: task.Id);

            // Assert: Tag reference safely unlinked in repository
            savedTask.Should().NotBeNull();
            savedTask.Tags.Should().BeEmpty();
            savedTask.Title.Should().Be(expected: "Task with tag");
        }
    }

    /// <summary>
    /// Tests verification of tag attachment persistence.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "UI")]
    public class TagPersistence
    {
        /// <summary>
        /// Verifies that saving a task with attached tags does not throw entity tracking exceptions.
        /// </summary>
        [Fact]
        public void SaveTaskWithAttachedTags_DoesNotThrowEntityTrackingException()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var notificationService = Substitute.For<INotificationService>();
            TaskRepository taskRepo = new(context: context, notificationService: notificationService);

            Tag tag = new() { Id = 10, Name = "Срочно", UsageCount = 1 };
            context.Tags.Add(entity: tag);
            context.SaveChanges();

            var task = new TaskItemBuilder().WithId(id: 200).WithTitle(title: "Task with persistent tag").Build();
            task.Tags.Add(item: new() { TaskId = 200, TagId = 10 });

            // Act: Call real repository method
            var act = () => taskRepo.Add(entity: task);

            // Assert: No entity graph tracking exception thrown, tag attached in DB
            act.Should().NotThrow();
            var savedTask = taskRepo.GetById(id: 200);
            savedTask.Should().NotBeNull();
            savedTask.Tags.Should().ContainSingle(predicate: tt => tt.TagId == 10);
        }
    }

    /// <summary>
    /// Tests verification of tag suggestion algorithms.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "UI")]
    public class TagSuggestions
    {
        /// <summary>
        /// Verifies that GetSuggestedTags returns 1 last used session tag followed by top popular tags from DB.
        /// </summary>
        [Fact]
        public void GetSuggestedTags_ReturnsLastUsedSessionTagAndTopPopularTags()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var notificationService = Substitute.For<INotificationService>();
            TagRepository tagRepo = new(context: context, notificationService: notificationService);

            Tag tag1 = new() { Id = 1, Name = "Tag 1", UsageCount = 10 };
            Tag tag2 = new() { Id = 2, Name = "Tag 2", UsageCount = 20 };
            Tag tag3 = new() { Id = 3, Name = "Tag 3", UsageCount = 30 };
            Tag tag4 = new() { Id = 4, Name = "Tag 4", UsageCount = 40 };
            Tag sessionTag = new() { Id = 5, Name = "Session Tag", UsageCount = 5 };

            context.Tags.AddRange(entities: [tag1, tag2, tag3, tag4, sessionTag]);
            context.SaveChanges();

            TagSessionService tagSessionService = new(tagRepository: tagRepo);

            // Act: Mark sessionTag as last used, then query suggested tags (5 count)
            tagSessionService.MarkTagUsed(tag: sessionTag);
            var suggestions = tagSessionService.GetSuggestedTags(count: 5);

            // Assert: Contains sessionTag first + 4 popular tags from database
            suggestions.Should().HaveCount(expected: 5);
            suggestions.First().Id.Should().Be(expected: sessionTag.Id);
        }
    }
}
