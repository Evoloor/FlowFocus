using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Data.Repositories.Helpers;
using FlowFocus.Data.Services;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Тесты инварианта «Условный ReadOnly для автоматических алгоритмов»:
/// Неактивные задачи (Completed, Irrelevant, NotConfigured) изолированы от любых
/// автоматических переносов, эскалаций, каскадов блокеров и смен статусов блокировки.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Planning")]
[Collection("StaticState")]
public class AutomationReadOnlyTaskTests : IntegrationTestBase
{
    [Fact]
    public void InactiveTasks_AreNeverMovedOrRescheduled_ByDistributeTasks()
    {
        // Arrange
        var yesterday = TodoDay.Today.Yesterday.ToDateTime();
        var today = TodoDay.Today.ToDateTime();

        var completed = new TaskItemBuilder().WithId(101).WithTitle("Завершенная")
            .WithScheduledDate(yesterday, DateSource.Manual).WithStatus(TaskStatus.Completed).Build();

        var irrelevant = new TaskItemBuilder().WithId(102).WithTitle("Неактуальная")
            .WithScheduledDate(today, DateSource.AutoFlexible).WithStatus(TaskStatus.Irrelevant).Build();

        var notConfigured = new TaskItemBuilder().WithId(103).WithTitle("Ненастроенная")
            .WithScheduledDate(null, DateSource.AutoFlexible).WithStatus(TaskStatus.NotConfigured).Build();

        TaskRepo.Add(completed);
        TaskRepo.Add(irrelevant);
        TaskRepo.Add(notConfigured);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(300).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedCompleted = TaskRepo.GetById(101);
        var savedIrrelevant = TaskRepo.GetById(102);
        var savedNotConfigured = TaskRepo.GetById(103);

        savedCompleted!.ScheduledDate.Should().Be(yesterday);
        savedCompleted.DateSource.Should().Be(DateSource.Manual);

        savedIrrelevant!.ScheduledDate.Should().Be(today);
        savedIrrelevant.DateSource.Should().Be(DateSource.AutoFlexible);

        savedNotConfigured!.ScheduledDate.Should().BeNull();
        savedNotConfigured.Status.Should().Be(TaskStatus.NotConfigured);
    }

    [Fact]
    public void InactiveTasks_NeverReceivePriorityEscalations_ViaActualizePriorities()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var urgentPriority = priorities[0]; // Order 1
        var lowPriority = priorities[3];    // Order 4

        var today = TodoDay.Today.ToDateTime();

        var completed = new TaskItemBuilder().WithId(201).WithPriorityId(lowPriority.Id).WithStatus(TaskStatus.Completed).Build();
        completed.PriorityEscalations.Add(new() { TargetPriorityId = urgentPriority.Id, EscalationDate = today });

        var irrelevant = new TaskItemBuilder().WithId(202).WithPriorityId(lowPriority.Id).WithStatus(TaskStatus.Irrelevant).Build();
        irrelevant.PriorityEscalations.Add(new() { TargetPriorityId = urgentPriority.Id, EscalationDate = today });

        var notConfigured = new TaskItemBuilder().WithId(203).WithPriorityId(lowPriority.Id).WithStatus(TaskStatus.NotConfigured).Build();
        notConfigured.PriorityEscalations.Add(new() { TargetPriorityId = urgentPriority.Id, EscalationDate = today });

        var activePlanned = new TaskItemBuilder().WithId(204).WithPriorityId(lowPriority.Id).WithStatus(TaskStatus.Planned).Build();
        activePlanned.PriorityEscalations.Add(new() { TargetPriorityId = urgentPriority.Id, EscalationDate = today });

        TaskRepo.Add(completed);
        TaskRepo.Add(irrelevant);
        TaskRepo.Add(notConfigured);
        TaskRepo.Add(activePlanned);

        // Act
        PlannerService.ActualizePriorities();
        TaskRepo.SaveChanges();

        // Assert
        TaskRepo.GetById(201)!.PriorityId.Should().Be(lowPriority.Id, "Completed задача не должна эскалироваться");
        TaskRepo.GetById(202)!.PriorityId.Should().Be(lowPriority.Id, "Irrelevant задача не должна эскалироваться");
        TaskRepo.GetById(203)!.PriorityId.Should().Be(lowPriority.Id, "NotConfigured задача не должна эскалироваться");
        TaskRepo.GetById(204)!.PriorityId.Should().Be(urgentPriority.Id, "Planned задача обязана эскалироваться");
    }

    [Fact]
    public void InactiveTasks_NeverMutatePriority_ViaNormalizeBlockerPriorities()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var urgentPriority = priorities[0];
        var lowPriority = priorities[3];

        // Ненастроенный блокер (низкий приоритет) блокирует срочную запланированную задачу
        var unconfiguredBlocker = new TaskItemBuilder().WithId(301).WithPriorityId(lowPriority.Id).WithStatus(TaskStatus.NotConfigured).Build();
        var plannedTarget = new TaskItemBuilder().WithId(302).WithPriorityId(urgentPriority.Id).WithStatus(TaskStatus.Planned).Build();

        Context.Tasks.AddRange(unconfiguredBlocker, plannedTarget);
        Context.TaskRelations.Add(new() { SourceTaskId = 301, TargetTaskId = 302, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act
        PlannerService.NormalizeBlockerPriorities();

        // Assert
        var savedBlocker = TaskRepo.GetById(301);
        savedBlocker!.PriorityId.Should().Be(lowPriority.Id, "Ненастроенный блокер является readonly для авто-нормализации приоритетов");
    }

    [Fact]
    public void InactiveTasks_NeverTransitionToBlockedStatus_ViaUpdateBlockedStatuses()
    {
        // Arrange: активный блокер и неактивное внешнее условие
        var activeBlocker = new TaskItemBuilder().WithId(401).WithStatus(TaskStatus.Planned).Build();
        var condition = ConditionRepo.GetOrCreate("Неактивное условие");
        // По умолчанию условие IsActive = false

        var notConfiguredTask = new TaskItemBuilder().WithId(402).WithStatus(TaskStatus.NotConfigured).Build();
        notConfiguredTask.Conditions.Add(new() { TaskId = 402, ConditionId = condition.Id });

        var completedTask = new TaskItemBuilder().WithId(403).WithStatus(TaskStatus.Completed).Build();
        completedTask.Conditions.Add(new() { TaskId = 403, ConditionId = condition.Id });

        var irrelevantTask = new TaskItemBuilder().WithId(404).WithStatus(TaskStatus.Irrelevant).Build();

        Context.Tasks.AddRange(activeBlocker, notConfiguredTask, completedTask, irrelevantTask);
        Context.TaskRelations.Add(new() { SourceTaskId = activeBlocker.Id, TargetTaskId = notConfiguredTask.Id, Type = RelationType.Blocks });
        Context.TaskRelations.Add(new() { SourceTaskId = activeBlocker.Id, TargetTaskId = irrelevantTask.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act: фоновый пересчет статусов блокировки
        PlannerService.UpdateBlockedStatuses();
        TaskRepo.SaveChanges();

        // Assert: ни одна из неактивных задач не получила статус Blocked
        TaskRepo.GetById(402)!.Status.Should().Be(TaskStatus.NotConfigured);
        TaskRepo.GetById(403)!.Status.Should().Be(TaskStatus.Completed);
        TaskRepo.GetById(404)!.Status.Should().Be(TaskStatus.Irrelevant);
    }

    [Fact]
    public void TaskDateNormalizer_NeverMutatesDateSource_ForInactiveTasksWithoutDate()
    {
        // Arrange
        var completedWithoutDate = new TaskItemBuilder().WithId(501)
            .WithScheduledDate(null, DateSource.Manual).WithStatus(TaskStatus.Completed).Build();

        var notConfiguredWithoutDate = new TaskItemBuilder().WithId(502)
            .WithScheduledDate(null, DateSource.AutoFixed).WithStatus(TaskStatus.NotConfigured).Build();

        var plannedWithoutDate = new TaskItemBuilder().WithId(503)
            .WithScheduledDate(null, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();

        Context.Tasks.AddRange(completedWithoutDate, notConfiguredWithoutDate, plannedWithoutDate);
        Context.SaveChanges();

        // Act
        TaskDateNormalizer.NormalizeDateSources(Context, new TaskRecurrenceService());
        Context.SaveChanges();

        // Assert
        var savedCompleted = Context.Tasks.Find(501);
        var savedNotConfigured = Context.Tasks.Find(502);
        var savedPlanned = Context.Tasks.Find(503);

        savedCompleted!.DateSource.Should().Be(DateSource.Manual, "Completed задача не должна нормализоваться в AutoFlexible");
        savedNotConfigured!.DateSource.Should().Be(DateSource.AutoFixed, "NotConfigured задача не должна нормализоваться в AutoFlexible");
        savedPlanned!.DateSource.Should().Be(DateSource.AutoFlexible, "Активная Planned задача без даты обязана нормализоваться");
    }

    [Fact]
    public void LinkingBlockerToNotConfiguredTask_KeepsTargetTaskNotConfigured_UntilConfiguredByUser()
    {
        // Сценарий: Пользователь в интерфейсе Задачи Б указывает не настроенную Задачу А как заблокированную
        // 1. Arrange & Act 1
        var taskB = new TaskItemBuilder().WithId(601).WithTitle("Задача Б (Блокер)").WithStatus(TaskStatus.Planned).Build();
        var taskA = new TaskItemBuilder().WithId(602).WithTitle("Задача А (Быстрый ввод)").WithStatus(TaskStatus.NotConfigured).Build();

        Context.Tasks.AddRange(taskB, taskA);
        Context.TaskRelations.Add(new() { SourceTaskId = taskB.Id, TargetTaskId = taskA.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Прогон полного цикла планировщика не должен мутировать А в Blocked
        PlannerService.RecalculateAll(new UserSettingsBuilder().Build());

        var taskABeforeConfig = TaskRepo.GetById(602);
        taskABeforeConfig!.Status.Should().Be(TaskStatus.NotConfigured, "До настройки пользователем статус остаётся NotConfigured");

        // 2. Act 2: Пользователь настраивает Задачу А (переводит в Planned и сохраняет)
        taskABeforeConfig.Title = "Задача А (Настроенная)";
        taskABeforeConfig.Status = TaskStatus.Planned;
        TaskRepo.Update(taskABeforeConfig);

        // Assert: при активном блокаторе Б статус становится Blocked
        var taskAAfterConfig = TaskRepo.GetById(602);
        taskAAfterConfig!.Status.Should().Be(TaskStatus.Blocked, "После настройки при активном блокаторе статус переходит в Blocked");
    }

    [Fact]
    public void ConfiguringNotConfiguredTask_TransitionsToPlanned_WhenBlockerIsCompleted()
    {
        // Arrange: Завершённый блокер Б и ненастроенная А
        var completedBlockerB = new TaskItemBuilder().WithId(701).WithTitle("Блокер Б").WithStatus(TaskStatus.Completed).Build();
        var taskA = new TaskItemBuilder().WithId(702).WithTitle("Задача А").WithStatus(TaskStatus.NotConfigured).Build();

        Context.Tasks.AddRange(completedBlockerB, taskA);
        Context.TaskRelations.Add(new() { SourceTaskId = completedBlockerB.Id, TargetTaskId = taskA.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act: Пользователь настраивает Задачу А
        var taskToConfigure = TaskRepo.GetById(702)!;
        taskToConfigure.Title = "Задача А (Настроена)";
        taskToConfigure.Status = TaskStatus.Planned;
        TaskRepo.Update(taskToConfigure);

        // Assert: блокер завершён -> задача становится Planned
        var savedTaskA = TaskRepo.GetById(702);
        savedTaskA!.Status.Should().Be(TaskStatus.Planned, "Если блокер завершён, настроенная задача переходит в Planned");
    }

    [Fact]
    public void ConfiguringNotConfiguredTask_TransitionsToBlocked_WhenInactiveConditionExists()
    {
        // Arrange: Неактивное условие и ненастроенная задача
        var condition = ConditionRepo.GetOrCreate("Требуется хорошая погода");
        // IsActive = false

        var taskA = new TaskItemBuilder().WithId(801).WithTitle("Задача А").WithStatus(TaskStatus.NotConfigured).Build();
        taskA.Conditions.Add(new() { TaskId = 801, ConditionId = condition.Id });
        Context.Tasks.Add(taskA);
        Context.SaveChanges();

        // Act: Пользователь настраивает Задачу А
        var taskToConfigure = TaskRepo.GetById(801)!;
        taskToConfigure.Status = TaskStatus.Planned;
        TaskRepo.Update(taskToConfigure);

        // Assert: условие неактивно -> задача переходит в Blocked
        var savedTaskA = TaskRepo.GetById(801);
        savedTaskA!.Status.Should().Be(TaskStatus.Blocked, "При наличии неактивного условия настроенная задача переходит в Blocked");
    }

    [Fact]
    public void UpdatingNotConfiguredTaskWithoutStatusChange_PreservesNotConfiguredStatus()
    {
        // Arrange: задача NotConfigured с блокером
        var blocker = new TaskItemBuilder().WithId(901).WithStatus(TaskStatus.Planned).Build();
        var notConfigured = new TaskItemBuilder().WithId(902).WithStatus(TaskStatus.NotConfigured).Build();

        Context.Tasks.AddRange(blocker, notConfigured);
        Context.TaskRelations.Add(new() { SourceTaskId = 901, TargetTaskId = 902, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act: обновление только названия без смены статуса
        var taskFromDb = TaskRepo.GetById(902)!;
        taskFromDb.Title = "Обновлённое название, но всё ещё ненастроенная";
        TaskRepo.Update(taskFromDb);

        // Assert: статус остаётся NotConfigured
        var reloaded = TaskRepo.GetById(902);
        reloaded!.Status.Should().Be(TaskStatus.NotConfigured, "Репозиторий не должен форсировать Blocked, пока задача остаётся NotConfigured");
    }
}
