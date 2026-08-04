using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Тесты на пересчёт дня с разнообразным набором задач (Manual, AutoFixed, AutoFlexible)
/// и проверку применения дневных лимитов (время, сложность, количество).
/// </summary>
[UsedImplicitly]
[Trait("Category", "Planning")]
[Collection("StaticState")]
public class RecalculationDailyLimitsTests : IntegrationTestBase
{
    private readonly DateTime _today = TodoDay.Today.ToDateTime();
    private readonly DateTime _tomorrow = TodoDay.Today.Tomorrow.ToDateTime();

    /// <summary>
    /// Проверяет, что если фиксированные задачи (Manual и recurring AutoFixed) превышают или достигают
    /// любого из 3 дневных лимитов (время, сложность, количество задач),
    /// то после пересчёта на сегодня не остаётся НИ ОДНОЙ AutoFlexible задачи.
    /// </summary>
    [Theory]
    [InlineData("TimeLimit")]
    [InlineData("ComplexityLimit")]
    [InlineData("TaskCountLimit")]
    public void Recalculate_WhenFixedTasksExceedOrReachDailyLimit_RemovesAutoFlexibleTasksFromToday(string limitType)
    {
        // Arrange
        UserSettings settings;
        TaskItem manualTask;
        TaskItem recurringAutoFixedTask;
        TaskItem autoFlexibleTask;

        switch (limitType)
        {
            case "TimeLimit":
                // Лимит по времени: 120 минут. Фиксированные задачи суммарно = 130 минут (> 120)
                settings = new UserSettingsBuilder().WithDailyTimeLimit(120).WithDailyComplexityLimit(1000).WithDailyTaskLimit(10).Build();
                manualTask = new TaskItemBuilder().WithId(101).WithTitle("Manual Task").WithEstimatedMinutes(70).WithComplexity(10)
                    .WithScheduledDate(_today, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();
                recurringAutoFixedTask = new TaskItemBuilder().WithId(102).WithTitle("AutoFixed Recurring Task").WithEstimatedMinutes(60).WithComplexity(10)
                    .WithScheduledDate(_today, DateSource.AutoFixed).WithStatus(TaskStatus.Planned).Build();
                break;

            case "ComplexityLimit":
                // Лимит по сложности: 50. Фиксированные задачи суммарно = 60 (> 50)
                settings = new UserSettingsBuilder().WithDailyTimeLimit(1000).WithDailyComplexityLimit(50).WithDailyTaskLimit(10).Build();
                manualTask = new TaskItemBuilder().WithId(101).WithTitle("Manual Task").WithEstimatedMinutes(10).WithComplexity(30)
                    .WithScheduledDate(_today, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();
                recurringAutoFixedTask = new TaskItemBuilder().WithId(102).WithTitle("AutoFixed Recurring Task").WithEstimatedMinutes(10).WithComplexity(30)
                    .WithScheduledDate(_today, DateSource.AutoFixed).WithStatus(TaskStatus.Planned).Build();
                break;

            case "TaskCountLimit":
            default:
                // Лимит по количеству: 2 задачи. Фиксированные задачи = 2 задачи (= лимиту)
                settings = new UserSettingsBuilder().WithDailyTimeLimit(1000).WithDailyComplexityLimit(1000).WithDailyTaskLimit(2).Build();
                manualTask = new TaskItemBuilder().WithId(101).WithTitle("Manual Task").WithEstimatedMinutes(10).WithComplexity(10)
                    .WithScheduledDate(_today, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();
                recurringAutoFixedTask = new TaskItemBuilder().WithId(102).WithTitle("AutoFixed Recurring Task").WithEstimatedMinutes(10).WithComplexity(10)
                    .WithScheduledDate(_today, DateSource.AutoFixed).WithStatus(TaskStatus.Planned).Build();
                break;
        }

        // Авто-флекс задача, которая изначально была назначена на сегодня
        autoFlexibleTask = new TaskItemBuilder().WithId(103).WithTitle("AutoFlexible Task").WithEstimatedMinutes(20).WithComplexity(10)
            .WithScheduledDate(_today, DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(manualTask);
        TaskRepo.Add(recurringAutoFixedTask);
        TaskRepo.Add(autoFlexibleTask);

        // Act
        PlannerService.RecalculateAll(settings);

        // Assert
        var updatedManual = TaskRepo.GetById(101);
        var updatedFixed = TaskRepo.GetById(102);
        var updatedFlexible = TaskRepo.GetById(103);

        // Фиксированные задачи остаются на сегодня
        updatedManual!.ScheduledDate.Should().Be(_today);
        updatedFixed!.ScheduledDate.Should().Be(_today);

        // Ни одна автофлекс задача не остаётся на сегодня
        updatedFlexible!.ScheduledDate.Should().Be(_tomorrow);
    }

    /// <summary>
    /// Проверяет, что если дневной лимит не превышен фиксированными задачами,
    /// то на сегодня назначится ровно столько автофлекс задач, сколько вмещается в лимиты.
    /// </summary>
    [Fact]
    public void Recalculate_WhenLimitsNotExceeded_AssignsAsManyAutoFlexibleTasksAsFitInRemainingCapacity()
    {
        // Arrange
        // Лимиты: Время = 180 мин, Сложность = 100, Задач = 4
        var settings = new UserSettingsBuilder()
            .WithDailyTimeLimit(180)
            .WithDailyComplexityLimit(100)
            .WithDailyTaskLimit(4)
            .Build();

        // Фиксированные задачи (Занимают 100 мин, 40 сложности, 2 задачи):
        var manualTask = new TaskItemBuilder().WithId(201).WithTitle("Manual Task")
            .WithEstimatedMinutes(60).WithComplexity(25)
            .WithScheduledDate(_today, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();

        var autoFixedTask = new TaskItemBuilder().WithId(202).WithTitle("AutoFixed Task")
            .WithEstimatedMinutes(40).WithComplexity(15)
            .WithScheduledDate(_today, DateSource.AutoFixed).WithStatus(TaskStatus.Planned).Build();

        // Кандидаты AutoFlexible:
        // Задача 1: 30 мин, 20 слож -> Сумма 130 мин, 60 слож, 3 задачи (ВЛЕЗАЕТ)
        var flexTask1 = new TaskItemBuilder().WithId(203).WithTitle("Flex 1")
            .WithEstimatedMinutes(30).WithComplexity(20)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        // Задача 2: 40 мин, 20 слож -> Сумма 170 мин, 80 слож, 4 задачи (ВЛЕЗАЕТ - ровно 4 задачи!)
        var flexTask2 = new TaskItemBuilder().WithId(204).WithTitle("Flex 2")
            .WithEstimatedMinutes(40).WithComplexity(20)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        // Задача 3: 30 мин, 10 слож -> Сумма 200 мин > 180 мин И 5 задач > 4 (НЕ ВЛЕЗАЕТ)
        var flexTask3 = new TaskItemBuilder().WithId(205).WithTitle("Flex 3")
            .WithEstimatedMinutes(30).WithComplexity(10)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(manualTask);
        TaskRepo.Add(autoFixedTask);
        TaskRepo.Add(flexTask1);
        TaskRepo.Add(flexTask2);
        TaskRepo.Add(flexTask3);

        // Act
        PlannerService.RecalculateAll(settings);

        // Assert
        TaskRepo.GetById(201)!.ScheduledDate.Should().Be(_today);
        TaskRepo.GetById(202)!.ScheduledDate.Should().Be(_today);
        TaskRepo.GetById(203)!.ScheduledDate.Should().Be(_today);
        TaskRepo.GetById(204)!.ScheduledDate.Should().Be(_today);

        // Flex 3 вытеснена на завтра, так как лимит исчерпан
        TaskRepo.GetById(205)!.ScheduledDate.Should().Be(_tomorrow);
    }

    /// <summary>
    /// Проверяет, что если новые/запланированные автофлекс задачи по сортировке приоритетнее
    /// имеющихся низкоприоритетных, то при пересчёте новые задачи вытесняют старые в соответствии с лимитами.
    /// </summary>
    [Fact]
    public void Recalculate_WhenHighPriorityAutoFlexibleTasksExist_DisplacesLowPriorityTasksFromToday()
    {
        // Arrange
        // Лимит по времени: 120 минут.
        var settings = new UserSettingsBuilder().WithDailyTimeLimit(120).WithDailyTaskLimit(10).Build();

        // В БД в системе 5 приоритетов: Id 2 (Высокий, Order 2), Id 4 (Низкий, Order 4).
        var highPriority = Context.Priorities.First(p => p.Order == 2);
        var lowPriority = Context.Priorities.First(p => p.Order == 4);

        // На сегодня назначена мануальная (60 мин) и старая низкоприоритетная автофлекс (50 мин)
        var manualTask = new TaskItemBuilder().WithId(301).WithTitle("Manual Task").WithEstimatedMinutes(60)
            .WithScheduledDate(_today, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();

        var existingLowPriorityFlex = new TaskItemBuilder().WithId(302).WithTitle("Low Priority Flex")
            .WithPriorityId(lowPriority.Id).WithEstimatedMinutes(50)
            .WithScheduledDate(_today, DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        // Появилась новая высокоприоритетная автофлекс задача (50 мин), пока без даты (или с AutoFlexible)
        var newHighPriorityFlex = new TaskItemBuilder().WithId(303).WithTitle("High Priority Flex")
            .WithPriorityId(highPriority.Id).WithEstimatedMinutes(50)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(manualTask);
        TaskRepo.Add(existingLowPriorityFlex);
        TaskRepo.Add(newHighPriorityFlex);

        // Act
        PlannerService.RecalculateAll(settings);

        // Assert
        // Мануальная остаётся на сегодня (60 мин)
        TaskRepo.GetById(301)!.ScheduledDate.Should().Be(_today);

        // Высокоприоритетная вытесняет низкоприоритетную (60 + 50 = 110 <= 120 мин)
        TaskRepo.GetById(303)!.ScheduledDate.Should().Be(_today);

        // Низкоприоритетная вытеснена на завтра (110 + 50 = 160 > 120 мин)
        TaskRepo.GetById(302)!.ScheduledDate.Should().Be(_tomorrow);
    }

    /// <summary>
    /// Проверяет, что если существуют автофлекс задачи, которые по алгоритму сортировки должны выполняться
    /// ПОСЛЕ имеющихся низкоприоритетных задач, то они заполняются в соответствии с лимитами уже ПОСЛЕ них.
    /// </summary>
    [Fact]
    public void Recalculate_WhenLowerPriorityAutoFlexibleTasksExist_FillsScheduleAfterExistingTasks()
    {
        // Arrange
        // Лимит времени: 160 минут
        var settings = new UserSettingsBuilder().WithDailyTimeLimit(160).WithDailyTaskLimit(10).Build();

        var mediumPriority = Context.Priorities.First(p => p.Order == 3);
        var backgroundPriority = Context.Priorities.First(p => p.Order == 5);

        // Мануальная задача (50 мин)
        var manualTask = new TaskItemBuilder().WithId(401).WithTitle("Manual Task").WithEstimatedMinutes(50)
            .WithScheduledDate(_today, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();

        // Средняя автофлекс (50 мин)
        var mediumFlex = new TaskItemBuilder().WithId(402).WithTitle("Medium Flex")
            .WithPriorityId(mediumPriority.Id).WithEstimatedMinutes(50)
            .WithScheduledDate(_today, DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        // Низшая / фоновая автофлекс 1 (50 мин) -> (50 + 50 + 50 = 150 <= 160) - Влезает ПОСЛЕ средней
        var lowerFlex1 = new TaskItemBuilder().WithId(403).WithTitle("Lower Flex 1")
            .WithPriorityId(backgroundPriority.Id).WithEstimatedMinutes(50)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        // Низшая / фоновая автофлекс 2 (50 мин) -> (150 + 50 = 200 > 160) - НЕ влезает
        var lowerFlex2 = new TaskItemBuilder().WithId(404).WithTitle("Lower Flex 2")
            .WithPriorityId(backgroundPriority.Id).WithEstimatedMinutes(50)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(manualTask);
        TaskRepo.Add(mediumFlex);
        TaskRepo.Add(lowerFlex1);
        TaskRepo.Add(lowerFlex2);

        // Act
        PlannerService.RecalculateAll(settings);

        // Assert
        TaskRepo.GetById(401)!.ScheduledDate.Should().Be(_today);
        TaskRepo.GetById(402)!.ScheduledDate.Should().Be(_today);

        // lowerFlex1 заполняется после mediumFlex на сегодня
        TaskRepo.GetById(403)!.ScheduledDate.Should().Be(_today);

        // lowerFlex2 превышает лимит и отправляется на завтра
        TaskRepo.GetById(404)!.ScheduledDate.Should().Be(_tomorrow);
    }
}
