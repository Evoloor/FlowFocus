using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
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
    /// Проверяет, что мануал и автофикс съедают лимиты, но не переносятся из-за них, 
    /// вытесняя AutoFlexible задачи на следующие дни.
    /// </summary>
    [Theory]
    //           [Лимиты дня: Время, Сложн, Кол-во] | [Мануал: Время, Сложн] | [Автофикс: Время, Сложн] | [Флекс: Время, Сложн]
    // 1. Превышение по времени (70 + 60 = 130 > 120)
    [InlineData(120, 1000, 10, 70, 10, 60, 10, 20, 10)]
    // 2. Превышение по сложности (30 + 30 = 60 > 50)
    [InlineData(1000, 50, 10, 10, 30, 10, 30, 20, 10)]
    // 3. Превышение по количеству (2 фиксированные = лимиту в 2 задачи, 3-я не влезет)
    [InlineData(1000, 1000, 2, 10, 10, 10, 10, 20, 10)]
    public void FixedTasks_ConsumeLimitsButNeverMove_PushingFlexibleTasksToTomorrow(
        int timeLimit, int compLimit, int countLimit,
        int manTime, int manComp,
        int fixTime, int fixComp,
        int flexTime, int flexComp)
    {
        // Arrange: Задаем лимиты из параметров InlineData
        var settings = new UserSettingsBuilder()
            .WithDailyTimeLimit(timeLimit).WithDailyComplexityLimit(compLimit).WithDailyTaskLimit(countLimit).Build();

        var manualTask = new TaskItemBuilder().WithId(101).WithScheduledDate(_today, DateSource.Manual)
            .WithEstimatedMinutes(manTime).WithComplexity(manComp).WithStatus(TaskStatus.Planned).Build();

        var autoFixedTask = new TaskItemBuilder().WithId(102).WithScheduledDate(_today, DateSource.AutoFixed)
            .WithEstimatedMinutes(fixTime).WithComplexity(fixComp).WithStatus(TaskStatus.Planned).Build();

        var flexTask = new TaskItemBuilder().WithId(103).WithDateSource(DateSource.AutoFlexible)
            .WithEstimatedMinutes(flexTime).WithComplexity(flexComp).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(manualTask);
        TaskRepo.Add(autoFixedTask);
        TaskRepo.Add(flexTask);

        // Act
        PlannerService.RecalculateAll(settings);

        // Assert: Мануал и автофикс должны всё ещё съедать лимиты, просто не переноситься из-за них
        TaskRepo.GetById(101)!.ScheduledDate.Should().Be(_today, "Manual задачи не сдвигаются алгоритмом");
        TaskRepo.GetById(102)!.ScheduledDate.Should().Be(_today, "AutoFixed задачи не сдвигаются алгоритмом");

        TaskRepo.GetById(103)!.ScheduledDate.Should().Be(_tomorrow,
            "AutoFlexible задача вытеснена, т.к. фиксированные задачи съели весь лимит");
    }

    /// <summary>
    /// Проверяет, что если новые/запланированные автофлекс задачи по сортировке приоритетнее
    /// имеющихся низкоприоритетных, то при пересчёте новые вытесняют старые в соответствии с лимитами.
    /// </summary>
    [Fact]
    public void Recalculate_WhenHighPriorityAutoFlexibleTasksExist_DisplacesLowPriorityTasksFromToday()
    {
        // Arrange
        var settings = new UserSettingsBuilder().WithDailyTimeLimit(120).Build();

        var highPriority = Context.Priorities.First(p => p.Order == 2);
        var lowPriority = Context.Priorities.First(p => p.Order == 4);

        // Фиксированная задача съедает половину лимита (60 мин)
        var manualTask = new TaskItemBuilder().WithId(301).WithEstimatedMinutes(60)
            .WithScheduledDate(_today, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();

        // Старая задача низкого приоритета (50 мин)
        var existingLowPriorityFlex = new TaskItemBuilder().WithId(302).WithPriorityId(lowPriority.Id)
            .WithEstimatedMinutes(50).WithScheduledDate(_today, DateSource.AutoFlexible).WithStatus(TaskStatus.Planned)
            .Build();

        // Новая задача высокого приоритета (50 мин)
        var newHighPriorityFlex = new TaskItemBuilder().WithId(303).WithPriorityId(highPriority.Id)
            .WithEstimatedMinutes(50).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(manualTask);
        TaskRepo.Add(existingLowPriorityFlex);
        TaskRepo.Add(newHighPriorityFlex);

        // Act
        PlannerService.RecalculateAll(settings);

        // Assert
        TaskRepo.GetById(301)!.ScheduledDate.Should().Be(_today, "Фиксированная задача остается на месте");
        TaskRepo.GetById(303)!.ScheduledDate.Should().Be(_today, "Высокий приоритет занимает оставшиеся 60 минут");
        TaskRepo.GetById(302)!.ScheduledDate.Should().Be(_tomorrow, "Низкий приоритет вытесняется на завтра");
    }

    /// <summary>
    /// Проверяет, что автофлекс задачи с низким приоритетом заполняют расписание ПОСЛЕ 
    /// более важных задач, переносясь на завтра при исчерпании лимита.
    /// </summary>
    [Fact]
    public void Recalculate_WhenLowerPriorityAutoFlexibleTasksExist_FillsScheduleAfterExistingTasks()
    {
        // Arrange
        var settings = new UserSettingsBuilder().WithDailyTimeLimit(160).Build();

        var mediumPriority = Context.Priorities.First(p => p.Order == 3);
        var backgroundPriority = Context.Priorities.First(p => p.Order == 5);

        // Мануальная + Средняя Флекс = 100 минут (остается 60 минут лимита)
        var manualTask = new TaskItemBuilder().WithId(401).WithEstimatedMinutes(50)
            .WithScheduledDate(_today, DateSource.Manual).WithStatus(TaskStatus.Planned).Build();
        var mediumFlex = new TaskItemBuilder().WithId(402).WithPriorityId(mediumPriority.Id).WithEstimatedMinutes(50)
            .WithScheduledDate(_today, DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        // Две фоновые задачи по 50 минут (влезет только первая)
        var lowerFlex1 = new TaskItemBuilder().WithId(403).WithPriorityId(backgroundPriority.Id)
            .WithEstimatedMinutes(50)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
        var lowerFlex2 = new TaskItemBuilder().WithId(404).WithPriorityId(backgroundPriority.Id)
            .WithEstimatedMinutes(50)
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
        TaskRepo.GetById(403)!.ScheduledDate.Should().Be(_today, "Влезает в остаток лимита (150 <= 160)");
        TaskRepo.GetById(404)!.ScheduledDate.Should().Be(_tomorrow, "Не влезает в лимит и переносится");
    }
}