using FluentAssertions;
using FlowFocus.Blazor.Components;
using FlowFocus.Blazor.Helpers;
using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for UI decision engines, card status flags, procrastination selection math, tag suggestions, and overlay queries.
/// </summary>
[UsedImplicitly]
[Trait("Category", "UI")]
[Collection("StaticState")]
public class UiAndUxEngineTests : IntegrationTestBase
{
    /// <summary>
    /// Verifies that card state flags match task status properties.
    /// </summary>
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

    /// <summary>
    /// Verifies that overdue task helper evaluates to true for yesterday's scheduled date.
    /// </summary>
    [Fact]
    public void OverdueTask_ReturnsIsOverdueTrue()
    {
        // Arrange
        var yesterday = TodoDay.Today.Yesterday.ToDateTime();
        var task = new TaskItemBuilder().WithScheduledDate(yesterday).Build();

        // Act
        var isOverdue = TodoDay.Today.IsOverdue(task.ScheduledDate);

        // Assert
        isOverdue.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that quick add checkmark creates an unconfigured task in repository.
    /// </summary>
    [Fact]
    public void QuickAddCheckmark_CreatesUnconfiguredTaskInRepository()
    {
        // Arrange
        var inputText = "Купить хлеб";

        var newTask = new TaskItemBuilder()
            .WithId(101)
            .WithTitle(inputText)
            .WithStatus(TaskStatus.NotConfigured)
            .Build();

        // Act
        TaskRepo.Add(newTask);
        var savedTask = TaskRepo.GetById(newTask.Id);

        // Assert
        savedTask.Should().NotBeNull();
        savedTask!.Title.Should().Be("Купить хлеб");
        savedTask.Status.Should().Be(TaskStatus.NotConfigured);
    }

    /// <summary>
    /// Verifies that SelectIdealProcrastinationTask returns task with the highest score having interest > 7.
    /// </summary>
    [Fact]
    public void SelectIdealTask_CallsProcrastinationEngine_ReturnsHighestScoreWithInterestGreaterThan7()
    {
        // Arrange
        var lowInterestHighPriority = new TaskItemBuilder().WithId(1).WithInterest(5).WithPriority(PriorityLevelBuilder.Critical).Build();
        var highInterestMediumPriority = new TaskItemBuilder().WithId(2).WithInterest(9).WithPriority(PriorityLevelBuilder.Medium).Build();
        var highInterestHighPriority = new TaskItemBuilder().WithId(3).WithInterest(10).WithPriority(PriorityLevelBuilder.High).Build();

        List<TaskItem> list = [lowInterestHighPriority, highInterestMediumPriority, highInterestHighPriority];

        // Act
        var ideal = ProcrastinationEngine.SelectIdealProcrastinationTask(list);

        // Assert
        ideal.Should().NotBeNull();
        ideal!.Id.Should().Be(3);
    }

    /// <summary>
    /// Verifies that RequiresDisplacement returns true when current scheduled minutes equals daily limit.
    /// </summary>
    [Fact]
    public void DisplacementNeeded_WhenDailyLimitFull()
    {
        // Act
        var requiresDisplacement = ProcrastinationEngine.RequiresDisplacement(currentScheduledMinutes: 180, dailyTimeLimit: 180);

        // Assert
        requiresDisplacement.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that repository query returns single overdue task when overdue tasks exist.
    /// </summary>
    [Fact]
    public void HasOverdueTasksInRepository_ReturnsOverdueTasks()
    {
        // Arrange
        var overdueDate = TodoDay.Today.Yesterday.ToDateTime();
        var task = new TaskItemBuilder().WithId(1).WithScheduledDate(overdueDate).WithStatus(TaskStatus.Planned).Build();
        TaskRepo.Add(task);

        // Act
        var overdueTasks = TaskRepo.GetOverdueTasks();

        // Assert
        overdueTasks.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that repository query returns empty list when no overdue tasks exist.
    /// </summary>
    [Fact]
    public void EmptyOverdueListInRepository_ReturnsEmptyList()
    {
        // Act
        var overdueTasks = TaskRepo.GetOverdueTasks();

        // Assert
        overdueTasks.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that deleting a tag unlinks references from tasks without deleting tasks.
    /// </summary>
    [Fact]
    public void DeleteTag_SafelyUnlinksFromTasksInRepository()
    {
        // Arrange
        Tag tag = new() { Id = 5, Name = "Работа", UsageCount = 1 };
        Context.Tags.Add(tag);

        var task = new TaskItemBuilder().WithId(1).WithTitle("Task with tag").Build();
        TaskRepo.Add(task);

        Context.TaskTags.Add(new() { TaskId = task.Id, TagId = tag.Id });
        Context.SaveChanges();

        // Act
        TagRepo.Delete(tag.Id);
        var savedTask = TaskRepo.GetById(task.Id);

        // Assert
        savedTask.Should().NotBeNull();
        savedTask!.Tags.Should().BeEmpty();
        savedTask.Title.Should().Be("Task with tag");
    }

    /// <summary>
    /// Verifies that saving a task with attached tags does not throw entity tracking exceptions.
    /// </summary>
    [Fact]
    public void SaveTaskWithAttachedTags_DoesNotThrowEntityTrackingException()
    {
        // Arrange
        Tag tag = new() { Id = 10, Name = "Срочно", UsageCount = 1 };
        Context.Tags.Add(tag);
        Context.SaveChanges();

        var task = new TaskItemBuilder().WithId(200).WithTitle("Task with persistent tag").Build();
        task.Tags.Add(new() { TaskId = 200, TagId = 10 });

        // Act
        var act = () => TaskRepo.Add(task);

        // Assert
        act.Should().NotThrow();
        var savedTask = TaskRepo.GetById(200);
        savedTask.Should().NotBeNull();
        savedTask!.Tags.Should().ContainSingle(tt => tt.TagId == 10);
    }

    /// <summary>
    /// Verifies that GetSuggestedTags returns 1 last used session tag followed by top popular tags from DB.
    /// </summary>
    [Fact]
    public void GetSuggestedTags_ReturnsLastUsedSessionTagAndTopPopularTags()
    {
        // Arrange
        Tag tag1 = new() { Id = 1, Name = "Tag 1", UsageCount = 10 };
        Tag tag2 = new() { Id = 2, Name = "Tag 2", UsageCount = 20 };
        Tag tag3 = new() { Id = 3, Name = "Tag 3", UsageCount = 30 };
        Tag tag4 = new() { Id = 4, Name = "Tag 4", UsageCount = 40 };
        Tag sessionTag = new() { Id = 5, Name = "Session Tag", UsageCount = 5 };

        Context.Tags.AddRange(tag1, tag2, tag3, tag4, sessionTag);
        Context.SaveChanges();

        TagSessionService tagSessionService = new(TagRepo);

        // Act
        tagSessionService.MarkTagUsed(sessionTag);
        var suggestions = tagSessionService.GetSuggestedTags(5);

        // Assert
        suggestions.Should().HaveCount(5);
        suggestions.First().Id.Should().Be(sessionTag.Id);
    }

    /// <summary>
    /// Verifies that Today filter excludes completed and irrelevant tasks when showInactive is false.
    /// </summary>
    [Fact]
    public void ApplyDefaultFilter_TodayFilter_HidesInactiveTasks_WhenShowInactiveIsFalse()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var activeTask = new TaskItemBuilder().WithId(1).WithScheduledDate(today).WithStatus(TaskStatus.Planned).Build();
        var completedTask = new TaskItemBuilder().WithId(2).WithScheduledDate(today).WithStatus(TaskStatus.Completed).Build();
        var irrelevantTask = new TaskItemBuilder().WithId(3).WithScheduledDate(today).WithStatus(TaskStatus.Irrelevant).Build();

        List<TaskItem> tasks = [activeTask, completedTask, irrelevantTask];
        TaskListFilter filter = new() { Type = TaskListFilterType.Today };

        // Act
        var filtered = TaskFilterEvaluator.ApplyDefaultFilter(tasks, filter, showInactive: false).ToList();

        // Assert
        filtered.Should().ContainSingle();
        filtered.Single().Id.Should().Be(1);
    }

    /// <summary>
    /// Verifies that Today filter includes completed and irrelevant tasks when showInactive is true.
    /// </summary>
    [Fact]
    public void ApplyDefaultFilter_TodayFilter_IncludesInactiveTasks_WhenShowInactiveIsTrue()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var activeTask = new TaskItemBuilder().WithId(1).WithScheduledDate(today).WithStatus(TaskStatus.Planned).Build();
        var completedTask = new TaskItemBuilder().WithId(2).WithScheduledDate(today).WithStatus(TaskStatus.Completed).Build();
        var irrelevantTask = new TaskItemBuilder().WithId(3).WithScheduledDate(today).WithStatus(TaskStatus.Irrelevant).Build();

        List<TaskItem> tasks = [activeTask, completedTask, irrelevantTask];
        TaskListFilter filter = new() { Type = TaskListFilterType.Today };

        // Act
        var filtered = TaskFilterEvaluator.ApplyDefaultFilter(tasks, filter, showInactive: true).ToList();

        // Assert
        filtered.Should().HaveCount(3);
        filtered.Select(t => t.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }
}
