using FluentAssertions;
using FlowFocus.Blazor.Components;
using FlowFocus.Blazor.Helpers;
using FlowFocus.Core;
using FlowFocus.Core.Helpers;
using FlowFocus.Core.Models;
using FlowFocus.Blazor.EditDialogContents;
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

    [Fact]
    public void GetSuggestedConditions_ReturnsLastUsedSessionConditionFirst_ThenPopularAlphabetically()
    {
        // Arrange
        var condA = new ExternalCondition { Id = 1, Name = "Бэта", UsageCount = 10 };
        var condB = new ExternalCondition { Id = 2, Name = "Альфа", UsageCount = 10 };
        var condLast = new ExternalCondition { Id = 3, Name = "Свежее", UsageCount = 1 };

        Context.ExternalConditions.AddRange(condA, condB, condLast);
        Context.SaveChanges();

        TagSessionService sessionService = new(TagRepo, ConditionRepo);

        // Act
        sessionService.MarkConditionUsed(condLast);
        var suggestions = sessionService.GetSuggestedConditions(5);

        // Assert
        suggestions.Should().HaveCount(3);
        suggestions[0].Id.Should().Be(condLast.Id);
        suggestions[1].Name.Should().Be("Альфа");
        suggestions[2].Name.Should().Be("Бэта");
    }

    [Fact]
    public void GetSuggestedConditions_WhenStoredUsageCountsAreZero_OrdersByActualTaskLinksAndLastUsedFirst()
    {
        // Arrange: имитация исторических данных в БД, где UsageCount == 0, но есть реальные связи TaskConditions
        var batumi = new ExternalCondition { Id = 10, Name = "Батуми", UsageCount = 0 };
        var devushka = new ExternalCondition { Id = 11, Name = "Девушка", UsageCount = 0 };
        var trati = new ExternalCondition { Id = 12, Name = "Траты", UsageCount = 0 };
        var poisk = new ExternalCondition { Id = 13, Name = "Поиск работы", UsageCount = 0 };
        Context.ExternalConditions.AddRange(batumi, devushka, trati, poisk);

        var nextTaskId = 1000;
        void AddTasksWithCondition(int condId, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var task = new TaskItemBuilder().WithId(nextTaskId++).WithTitle($"Task {nextTaskId}").Build();
                task.Conditions.Add(new() { TaskId = task.Id, ConditionId = condId });
                Context.Tasks.Add(task);
            }
        }

        AddTasksWithCondition(batumi.Id, 2);
        AddTasksWithCondition(devushka.Id, 1);
        AddTasksWithCondition(trati.Id, 5);
        AddTasksWithCondition(poisk.Id, 3);
        Context.SaveChanges();

        TagSessionService sessionService = new(TagRepo, ConditionRepo);

        // Act 1: без последнего выбранного — сортировка по реальному количеству задач
        var initial = sessionService.GetSuggestedConditions(5);

        // Assert 1
        initial.Select(c => c.Name).Should().ContainInOrder("Траты", "Поиск работы", "Батуми", "Девушка");

        // Act 2: выбрали "Девушка" -> оно должно стать первым, остальные по реальной популярности
        sessionService.MarkConditionUsed(devushka);
        var afterSelect = sessionService.GetSuggestedConditions(5);

        // Assert 2
        afterSelect.Select(c => c.Name).Should().ContainInOrder("Девушка", "Траты", "Поиск работы", "Батуми");
    }

    [Fact]
    public void GetSuggestedTags_WhenStoredUsageCountsAreZero_OrdersByActualTaskLinksAndLastUsedFirst()
    {
        // Arrange
        var tagA = new Tag { Id = 20, Name = "Ааа", UsageCount = 0 };
        var tagB = new Tag { Id = 21, Name = "Ббб", UsageCount = 0 };
        var tagPop = new Tag { Id = 22, Name = "Яяя", UsageCount = 0 };
        Context.Tags.AddRange(tagA, tagB, tagPop);

        var nextTaskId = 2000;
        void AddTasksWithTag(int tagId, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var task = new TaskItemBuilder().WithId(nextTaskId++).WithTitle($"TagTask {nextTaskId}").Build();
                task.Tags.Add(new() { TaskId = task.Id, TagId = tagId });
                Context.Tasks.Add(task);
            }
        }

        AddTasksWithTag(tagA.Id, 1);
        AddTasksWithTag(tagB.Id, 2);
        AddTasksWithTag(tagPop.Id, 4);
        Context.SaveChanges();

        TagSessionService sessionService = new(TagRepo, ConditionRepo);

        // Act 1
        var initial = sessionService.GetSuggestedTags(5);

        // Assert 1
        initial.Select(t => t.Name).Should().ContainInOrder("Яяя", "Ббб", "Ааа");

        // Act 2
        sessionService.MarkTagUsed(tagA);
        var afterSelect = sessionService.GetSuggestedTags(5);

        // Assert 2
        afterSelect.Select(t => t.Name).Should().ContainInOrder("Ааа", "Яяя", "Ббб");
    }

    [Fact]
    public void TaskFilterEvaluator_SortsAvailableTagsAndConditions_ByTaskCountDescThenNameAsc()
    {
        // Arrange
        Tag tagAlpha = new() { Id = 1, Name = "Alpha", UsageCount = 1 };
        Tag tagBeta = new() { Id = 2, Name = "Beta", UsageCount = 99 };
        Tag tagGamma = new() { Id = 3, Name = "Gamma", UsageCount = 50 };

        ExternalCondition condAlpha = new() { Id = 11, Name = "Дома", UsageCount = 1 };
        ExternalCondition condBeta = new() { Id = 12, Name = "В дороге", UsageCount = 99 };
        ExternalCondition condGamma = new() { Id = 13, Name = "Офис", UsageCount = 50 };

        var t1 = new TaskItemBuilder().WithId(1).Build();
        t1.Tags.AddRange([new() { TaskId = 1, TagId = 1, Tag = tagAlpha }, new() { TaskId = 1, TagId = 3, Tag = tagGamma }]);
        t1.Conditions.AddRange([new() { TaskId = 1, ConditionId = 11, Condition = condAlpha }, new() { TaskId = 1, ConditionId = 13, Condition = condGamma }]);

        var t2 = new TaskItemBuilder().WithId(2).Build();
        t2.Tags.AddRange([new() { TaskId = 2, TagId = 2, Tag = tagBeta }, new() { TaskId = 2, TagId = 3, Tag = tagGamma }]);
        t2.Conditions.AddRange([new() { TaskId = 2, ConditionId = 12, Condition = condBeta }, new() { TaskId = 2, ConditionId = 13, Condition = condGamma }]);

        List<TaskItem> tasks = [t1, t2];

        // Act
        var sortedTags = TaskFilterEvaluator.GetSortedAvailableTags(tasks);
        var sortedConditions = TaskFilterEvaluator.GetSortedAvailableConditions(tasks);

        // Assert: Gamma (2 tasks) first, then Alpha/Beta (1 task each) alphabetically
        sortedTags.Select(t => t.Name).Should().ContainInOrder("Gamma", "Alpha", "Beta");
        sortedConditions.Select(c => c.Name).Should().ContainInOrder("Офис", "В дороге", "Дома");
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
        filtered.Select(t => t.Id).Should().BeEquivalentTo([1, 2, 3]);
    }

    /// <summary>
    /// Verifies that PrivacyHelper.MaskText preserves the first letter and appends asterisks.
    /// </summary>
    [Theory]
    [InlineData("Купить хлеб", "К***")]
    [InlineData("Task title", "T***")]
    [InlineData("A", "A")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void PrivacyHelper_MaskText_PreservesFirstLetterAndAppendsAsterisks(string? input, string expected)
    {
        // Act
        var result = PrivacyHelper.MaskText(input);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that PrivacyHelper.MaskText correctly handles Unicode surrogate pairs (e.g. emoji).
    /// </summary>
    [Fact]
    public void PrivacyHelper_MaskText_HandlesSurrogatePairs()
    {
        // Arrange
        var emojiInput = "😀 test task";

        // Act
        var result = PrivacyHelper.MaskText(emojiInput);

        // Assert
        result.Should().Be("😀***");
    }

    /// <summary>
    /// Verifies that SubtaskDto retains HideUnderSpoiler property.
    /// </summary>
    [Fact]
    public void SubtaskDto_PreservesHideUnderSpoiler()
    {
        // Arrange
        SubtaskDto dto = new()
        {
            Title = "Секретная подзадача",
            HideUnderSpoiler = true
        };

        // Assert
        dto.HideUnderSpoiler.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that privacy mode setting (HideTaskTitlesDefault) persists in SettingsRepository across calls.
    /// </summary>
    [Fact]
    public void PrivacyMode_PersistsInSettingsRepository()
    {
        // Arrange
        SettingsRepository settingsRepo = new(Context, NotificationService);
        var settings = settingsRepo.GetUserSettings();
        settings.HideTaskTitlesDefault = true;

        // Act
        settingsRepo.UpdateSettings(settings);
        var reloadedSettings = settingsRepo.GetUserSettings();

        // Assert
        reloadedSettings.Should().NotBeNull();
        reloadedSettings.HideTaskTitlesDefault.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that WorkItemStyleHelper calculates unified CSS classes for both TaskItem and SubtaskItem.
    /// </summary>
    [Fact]
    public void WorkItemStyleHelper_GetCardClass_AppliesCorrectClassesForTaskAndSubtask()
    {
        // 1. Regular planned task
        var plannedTask = new TaskItemBuilder().WithStatus(TaskStatus.Planned).Build();
        var plannedClasses = WorkItemStyleHelper.GetCardClass(plannedTask);
        plannedClasses.Should().Be("task-card");

        // 2. Completed nested subtask
        var completedSubtask = new SubtaskItemBuilder().WithStatus(TaskStatus.Completed).Build();
        var subtaskClasses = WorkItemStyleHelper.GetCardClass(completedSubtask, isNested: true, displayMode: FlowFocus.Core.Enums.DisplayMode.Compact);
        subtaskClasses.Should().Contain("task-card");
        subtaskClasses.Should().Contain("task-card-completed");
        subtaskClasses.Should().Contain("task-card-nested");
        subtaskClasses.Should().Contain("task-card-compact");

        // 3. Blocked task
        var blockedTask = new TaskItemBuilder().WithStatus(TaskStatus.Blocked).Build();
        WorkItemStyleHelper.GetCardClass(blockedTask).Should().Contain("task-card-blocked");

        // 4. Irrelevant subtask
        var irrelevantSubtask = new SubtaskItemBuilder().WithStatus(TaskStatus.Irrelevant).Build();
        WorkItemStyleHelper.GetCardClass(irrelevantSubtask).Should().Contain("task-card-irrelevant");
    }

    /// <summary>
    /// Verifies that WorkItemStyleHelper calculates unified title classes for work items.
    /// </summary>
    [Fact]
    public void WorkItemStyleHelper_GetTitleClass_AppliesCorrectTitleClasses()
    {
        var completedSub = new SubtaskItemBuilder().WithStatus(TaskStatus.Completed).Build();
        WorkItemStyleHelper.GetTitleClass(completedSub).Should().Be("title-completed");

        var plannedTask = new TaskItemBuilder().WithStatus(TaskStatus.Planned).Build();
        WorkItemStyleHelper.GetTitleClass(plannedTask).Should().BeEmpty();

        var irrelevantTask = new TaskItemBuilder().WithStatus(TaskStatus.Irrelevant).Build();
        WorkItemStyleHelper.GetTitleClass(irrelevantTask).Should().Be("title-irrelevant");

        var notConfiguredTask = new TaskItemBuilder().WithStatus(TaskStatus.NotConfigured).Build();
        WorkItemStyleHelper.GetTitleClass(notConfiguredTask).Should().Be("title-not-configured");
    }
}
