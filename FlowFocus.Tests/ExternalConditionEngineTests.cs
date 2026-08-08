using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using Xunit;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class ExternalConditionEngineTests : IntegrationTestBase
{
    [Fact]
    public void TaskLabelBase_Inheritance_And_SOLID_CommonPropertiesExist()
    {
        // Arrange & Act
        TaskLabelBase tag = new Tag { Name = "Работа", BackgroundColor = "#123456", UsageCount = 5 };
        TaskLabelBase condition = new ExternalCondition { Name = "В городе А", BackgroundColor = "#654321", UsageCount = 2, IsActive = true };

        // Assert
        tag.Name.Should().Be("Работа");
        tag.BackgroundColor.Should().Be("#123456");
        tag.UsageCount.Should().Be(5);

        condition.Name.Should().Be("В городе А");
        condition.BackgroundColor.Should().Be("#654321");
        condition.UsageCount.Should().Be(2);
        ((ExternalCondition)condition).IsActive.Should().BeTrue();
    }

    [Fact]
    public void NewCondition_IsInactiveByDefault()
    {
        // Arrange & Act
        var conditionModel = new ExternalCondition { Name = "Новое условие" };
        var repoCondition = ConditionRepo.GetOrCreate("Условие из репозитория");

        // Assert
        conditionModel.IsActive.Should().BeFalse();
        repoCondition.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ToggleCondition_TogglesBlockedState_OnTask()
    {
        // Arrange
        var condition = ConditionRepo.GetOrCreate("В городе А");
        ConditionRepo.ToggleConditionActive(condition.Id, true); // Включаем условие для теста

        var task = new TaskItemBuilder()
            .WithId(101)
            .WithTitle("Купить продукты")
            .WithStatus(TaskStatus.Planned)
            .Build();

        task.Conditions.Add(new TaskCondition { TaskId = task.Id, ConditionId = condition.Id });
        Context.Tasks.Add(task);
        Context.SaveChanges();

        // Act 1: Toggle condition OFF
        ConditionRepo.ToggleConditionActive(condition.Id, false);

        // Assert 1: Task becomes Blocked
        var blockedTask = TaskRepo.GetById(task.Id);
        blockedTask.Should().NotBeNull();
        blockedTask!.Status.Should().Be(TaskStatus.Blocked);
        blockedTask.IsBlocked.Should().BeTrue();

        // Act 2: Toggle condition back ON
        ConditionRepo.ToggleConditionActive(condition.Id, true);

        // Assert 2: Task returns to Planned
        var unblockedTask = TaskRepo.GetById(task.Id);
        unblockedTask.Should().NotBeNull();
        unblockedTask!.Status.Should().Be(TaskStatus.Planned);
        unblockedTask.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void InactiveTasks_NotAffectedByConditionToggles()
    {
        // Arrange
        var condition = ConditionRepo.GetOrCreate("Наличие авто");
        ConditionRepo.ToggleConditionActive(condition.Id, true);

        var completedTask = new TaskItemBuilder().WithId(102).WithStatus(TaskStatus.Completed).Build();
        var irrelevantTask = new TaskItemBuilder().WithId(103).WithStatus(TaskStatus.Irrelevant).Build();

        completedTask.Conditions.Add(new TaskCondition { TaskId = completedTask.Id, ConditionId = condition.Id });
        irrelevantTask.Conditions.Add(new TaskCondition { TaskId = irrelevantTask.Id, ConditionId = condition.Id });

        Context.Tasks.AddRange(completedTask, irrelevantTask);
        Context.SaveChanges();

        // Act: Toggle condition OFF
        ConditionRepo.ToggleConditionActive(condition.Id, false);

        // Assert: Inactive tasks remain Completed / Irrelevant
        var t1 = TaskRepo.GetById(completedTask.Id);
        var t2 = TaskRepo.GetById(irrelevantTask.Id);

        t1!.Status.Should().Be(TaskStatus.Completed);
        t2!.Status.Should().Be(TaskStatus.Irrelevant);
    }

    [Fact]
    public void MultipleConditions_RequiresAllToBeActive()
    {
        // Arrange
        var condA = ConditionRepo.GetOrCreate("Город А");
        var condB = ConditionRepo.GetOrCreate("Выходной день");
        ConditionRepo.ToggleConditionActive(condA.Id, true);
        ConditionRepo.ToggleConditionActive(condB.Id, true);

        var task = new TaskItemBuilder().WithId(104).WithStatus(TaskStatus.Planned).Build();
        task.Conditions.Add(new TaskCondition { TaskId = task.Id, ConditionId = condA.Id });
        task.Conditions.Add(new TaskCondition { TaskId = task.Id, ConditionId = condB.Id });

        Context.Tasks.Add(task);
        Context.SaveChanges();

        // Act 1: Turn off Cond A -> Task becomes Blocked
        ConditionRepo.ToggleConditionActive(condA.Id, false);
        TaskRepo.GetById(task.Id)!.Status.Should().Be(TaskStatus.Blocked);

        // Act 2: Turn off Cond B as well
        ConditionRepo.ToggleConditionActive(condB.Id, false);
        TaskRepo.GetById(task.Id)!.Status.Should().Be(TaskStatus.Blocked);

        // Act 3: Turn on Cond A only (Cond B still OFF) -> Task MUST REMAIN Blocked
        ConditionRepo.ToggleConditionActive(condA.Id, true);
        TaskRepo.GetById(task.Id)!.Status.Should().Be(TaskStatus.Blocked);

        // Act 4: Turn on Cond B as well -> Now ALL conditions are active -> Task becomes Planned
        ConditionRepo.ToggleConditionActive(condB.Id, true);
        TaskRepo.GetById(task.Id)!.Status.Should().Be(TaskStatus.Planned);
    }

    [Fact]
    public void AutoFlexibleTask_ExcludedFromDistribution_WhenConditionInactive()
    {
        // Arrange
        var condition = ConditionRepo.GetOrCreate("Офис");
        ConditionRepo.ToggleConditionActive(condition.Id, false);

        var task = new TaskItemBuilder()
            .WithId(105)
            .WithTitle("Офисная задача")
            .WithStatus(TaskStatus.Planned)
            .WithDateSource(DateSource.AutoFlexible)
            .Build();

        task.Conditions.Add(new TaskCondition { TaskId = task.Id, ConditionId = condition.Id });
        Context.Tasks.Add(task);
        Context.SaveChanges();

        // Act: Run planner full recalculation (dates distribution + blocked status update)
        PlannerService.RecalculateAll(SettingsRepo.GetUserSettings());

        // Assert: ScheduledDate remains null because task is blocked by condition and status is Blocked
        var updated = TaskRepo.GetById(task.Id);
        updated!.ScheduledDate.Should().BeNull();
        updated.Status.Should().Be(TaskStatus.Blocked);
    }

    [Fact]
    public void ManualDateTask_RetainsScheduledDate_WhenBlockedByCondition()
    {
        // Arrange
        var condition = ConditionRepo.GetOrCreate("Интернет");
        ConditionRepo.ToggleConditionActive(condition.Id, false);

        var manualDate = DateTime.Today.AddDays(2);
        var task = new TaskItemBuilder()
            .WithId(106)
            .WithTitle("Задача с ручной датой")
            .WithStatus(TaskStatus.Planned)
            .WithScheduledDate(manualDate, DateSource.Manual)
            .Build();

        task.Conditions.Add(new TaskCondition { TaskId = task.Id, ConditionId = condition.Id });
        Context.Tasks.Add(task);
        Context.SaveChanges();

        // Act: Update blocked statuses
        PlannerService.UpdateBlockedStatuses();

        // Assert: Manual date is preserved, but status is Blocked
        var updated = TaskRepo.GetById(task.Id);
        updated!.ScheduledDate.Should().Be(manualDate);
        updated.DateSource.Should().Be(DateSource.Manual);
        updated.Status.Should().Be(TaskStatus.Blocked);
    }

    [Fact]
    public void RecurringTask_SilentSkip_ClearsScheduledDate_And_AutoSchedulesToToday_OnActivation()
    {
        // Arrange: Создаем повторяющуюся задачу с включенным условием, находящуюся на сегодня
        var condition = ConditionRepo.GetOrCreate("Дачный ПК");
        ConditionRepo.ToggleConditionActive(condition.Id, true);

        var todayDate = TodoDay.Today.ToDateTime();
        var recurringTask = new TaskItemBuilder()
            .WithId(107)
            .WithTitle("Повторяющаяся дачная задача")
            .WithStatus(TaskStatus.Planned)
            .WithRecurrence(RecurrenceType.Daily)
            .WithScheduledDate(todayDate, DateSource.AutoFixed)
            .Build();

        recurringTask.Conditions.Add(new TaskCondition { TaskId = recurringTask.Id, ConditionId = condition.Id });
        Context.Tasks.Add(recurringTask);
        Context.SaveChanges();

        // Act 1: Отключаем условие -> задача улетает из расписания (ScheduledDate = null)
        ConditionRepo.ToggleConditionActive(condition.Id, false);

        // Assert 1: Дата сброшена, статус Blocked, в просрочках отсутствует ("тихий пропуск")
        var blockedTask = TaskRepo.GetById(recurringTask.Id);
        blockedTask!.ScheduledDate.Should().BeNull();
        blockedTask.Status.Should().Be(TaskStatus.Blocked);

        var overdueWhileOff = TaskRepo.GetOverdueTasks();
        overdueWhileOff.Should().NotContain(t => t.Id == recurringTask.Id);

        // Act 2: Включаем условие обратно
        ConditionRepo.ToggleConditionActive(condition.Id, true);

        // Assert 2: Просроченное/очередное повторение назначается на Сегодня, статус возвращается в Planned
        var unblockedTask = TaskRepo.GetById(recurringTask.Id);
        unblockedTask!.Status.Should().Be(TaskStatus.Planned);
        unblockedTask.ScheduledDate.Should().NotBeNull();
        TodoDay.Today.IsSameDay(unblockedTask.ScheduledDate).Should().BeTrue();
    }

    [Fact]
    public void DeleteCondition_WithConfirmation_UnblocksTask_IfNoOtherBlockers()
    {
        // Arrange
        var cond1 = ConditionRepo.GetOrCreate("Условие 1");
        ConditionRepo.ToggleConditionActive(cond1.Id, false);

        var blockerTask = new TaskItemBuilder().WithId(201).WithStatus(TaskStatus.Planned).Build();
        var targetTask = new TaskItemBuilder().WithId(202).WithStatus(TaskStatus.Planned).Build();

        targetTask.Conditions.Add(new TaskCondition { TaskId = targetTask.Id, ConditionId = cond1.Id });

        Context.Tasks.AddRange(blockerTask, targetTask);
        Context.TaskRelations.Add(new TaskRelation { SourceTaskId = blockerTask.Id, TargetTaskId = targetTask.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Target task has BOTH condition block and task relation block
        PlannerService.UpdateBlockedStatuses();
        TaskRepo.GetById(targetTask.Id)!.Status.Should().Be(TaskStatus.Blocked);

        // Act 1: Delete condition -> task STILL blocked by relation
        ConditionRepo.DeleteCondition(cond1.Id);
        TaskRepo.GetById(targetTask.Id)!.Status.Should().Be(TaskStatus.Blocked);

        // Act 2: Complete blocker task -> Now target task has NO blockers left -> becomes Planned
        TaskRepo.CompleteTask(blockerTask.Id);
        PlannerService.UpdateBlockedStatuses();
        TaskRepo.GetById(targetTask.Id)!.Status.Should().Be(TaskStatus.Planned);
    }

    [Fact]
    public void ConditionLifecycle_NotDeletedOnTaskDetachment()
    {
        // Arrange
        var condition = ConditionRepo.GetOrCreate("Временный контекст");
        ConditionRepo.IncrementUsage(condition.Id);

        var task = new TaskItemBuilder().WithId(301).WithStatus(TaskStatus.Planned).Build();
        task.Conditions.Add(new TaskCondition { TaskId = task.Id, ConditionId = condition.Id });
        Context.Tasks.Add(task);
        Context.SaveChanges();

        // Act: Remove condition from task
        task.Conditions.Clear();
        TaskRepo.Update(task);
        ConditionRepo.DecrementUsage(condition.Id);

        // Assert: Unlike Tag, Condition remains in database when usage count reaches 0
        var foundInDb = ConditionRepo.GetById(condition.Id);
        foundInDb.Should().NotBeNull();
        foundInDb!.Name.Should().Be("Временный контекст");
    }

    [Fact]
    public void TaskFilterEvaluator_FiltersByConditionId_And_WithoutConditionsOption()
    {
        // Arrange
        var cond1 = new ExternalCondition { Id = 10, Name = "Город А" };
        var cond2 = new ExternalCondition { Id = 20, Name = "Офис" };

        var taskWithCond1 = new TaskItemBuilder().WithId(401).WithTitle("Задача в Городе А").Build();
        taskWithCond1.Conditions.Add(new TaskCondition { TaskId = 401, ConditionId = 10, Condition = cond1 });

        var taskWithCond2 = new TaskItemBuilder().WithId(402).WithTitle("Задача в Офисе").Build();
        taskWithCond2.Conditions.Add(new TaskCondition { TaskId = 402, ConditionId = 20, Condition = cond2 });

        var taskWithoutCond = new TaskItemBuilder().WithId(403).WithTitle("Задача без условий").Build();

        List<TaskItem> allTasks = [taskWithCond1, taskWithCond2, taskWithoutCond];

        // Act 1: Фильтрация по конкретному условию (cond1)
        var filteredByCond1 = FlowFocus.Blazor.Helpers.TaskFilterEvaluator.ApplySearchAndFilters(
            allTasks, searchQuery: "", dateRange: null, selectedStatuses: null, durationFilter: DurationFilter.All,
            selectedTagIds: [], hideWithDates: false, selectedConditionIds: [10]).ToList();

        // Assert 1: Возвращает только задачу с cond1
        filteredByCond1.Should().ContainSingle();
        filteredByCond1.Single().Id.Should().Be(401);

        // Act 2: Фильтрация по опции "Без условий" (id = 0 или null)
        var filteredWithoutCond = FlowFocus.Blazor.Helpers.TaskFilterEvaluator.ApplySearchAndFilters(
            allTasks, searchQuery: "", dateRange: null, selectedStatuses: null, durationFilter: DurationFilter.All,
            selectedTagIds: [], hideWithDates: false, selectedConditionIds: [0]).ToList();

        // Assert 2: Возвращает только задачу без условий
        filteredWithoutCond.Should().ContainSingle();
        filteredWithoutCond.Single().Id.Should().Be(403);

        // Act 3: Фильтрация комбинацией: cond2 И "Без условий" (id = 20 и 0)
        var filteredComb = FlowFocus.Blazor.Helpers.TaskFilterEvaluator.ApplySearchAndFilters(
            allTasks, searchQuery: "", dateRange: null, selectedStatuses: null, durationFilter: DurationFilter.All,
            selectedTagIds: [], hideWithDates: false, selectedConditionIds: [20, 0]).ToList();

        // Assert 3: Возвращает задачи 402 и 403
        filteredComb.Should().HaveCount(2);
        filteredComb.Select(t => t.Id).Should().BeEquivalentTo(new[] { 402, 403 });
    }
}
