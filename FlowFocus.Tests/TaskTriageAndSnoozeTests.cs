using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Data.Services;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class TaskTriageAndSnoozeTests : IntegrationTestBase
{
    private readonly TodoDay _today = TodoDay.Today;

    [Fact]
    public void Postpone_AutoFlexible_WithReplacement_DoesNotBoomerang_AndTakesNextTask()
    {
        // Arrange: Задача 1 с высоким приоритетом и Задача 2 в бэклоге
        var urgentPriority = Context.Priorities.First(p => p.Order == 1);
        var normalPriority = Context.Priorities.First(p => p.Order == 3);

        var taskA = new TaskItemBuilder()
            .WithId(1)
            .WithTitle("Task A (Urgent)")
            .WithPriority(urgentPriority)
            .WithEstimatedMinutes(60)
            .WithScheduledDate(_today.ToDateTime(), DateSource.AutoFlexible)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var taskB = new TaskItemBuilder()
            .WithId(2)
            .WithTitle("Task B (Normal)")
            .WithPriority(normalPriority)
            .WithEstimatedMinutes(60)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(taskA);
        TaskRepo.Add(taskB);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(60).Build();
        SettingsRepo.UpdateSettings(settings);

        // Act: Откладываем Задачу А с заменой из бэклога
        PlannerService.PostponeTask(taskA.Id, PostponeTarget.Auto, replaceWithBacklog: true, settings);

        // Assert:
        var savedA = TaskRepo.GetById(taskA.Id);
        var savedB = TaskRepo.GetById(taskB.Id);

        // Задача А не должна вернуться бумерангом на сегодня
        savedA!.ScheduledDate.Should().NotBe(_today.ToDateTime());
        savedA.SnoozedUntilDate.Should().Be(_today.Tomorrow.ToDateTime());

        // Задача Б должна занять освободившийся слот на сегодня
        savedB!.ScheduledDate.Should().Be(_today.ToDateTime());
    }

    [Fact]
    public void Postpone_AutoFlexible_WithoutReplacement_LocksDayAndDoesNotFillSlot()
    {
        // Arrange: Две задачи на сегодня, лимит на 2 часа
        var taskA = new TaskItemBuilder()
            .WithId(10)
            .WithEstimatedMinutes(60)
            .WithScheduledDate(_today.ToDateTime(), DateSource.AutoFlexible)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var taskB = new TaskItemBuilder()
            .WithId(20)
            .WithEstimatedMinutes(60)
            .WithScheduledDate(_today.ToDateTime(), DateSource.AutoFlexible)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var taskC = new TaskItemBuilder()
            .WithId(30)
            .WithEstimatedMinutes(60)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(taskA);
        TaskRepo.Add(taskB);
        TaskRepo.Add(taskC);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(120).Build();
        SettingsRepo.UpdateSettings(settings);

        // Act: Откладываем задачу А без замены (разгружаем день)
        PlannerService.PostponeTask(taskA.Id, PostponeTarget.Auto, replaceWithBacklog: false, settings);

        // Assert:
        var savedA = TaskRepo.GetById(taskA.Id);
        var savedB = TaskRepo.GetById(taskB.Id);
        var savedC = TaskRepo.GetById(taskC.Id);

        // Задача А снята с сегодня
        savedA!.ScheduledDate.Should().NotBe(_today.ToDateTime());

        // Задача B осталась на сегодня
        savedB!.ScheduledDate.Should().Be(_today.ToDateTime());

        // Задача C НЕ должна была досыпаться на сегодня
        savedC!.ScheduledDate.Should().NotBe(_today.ToDateTime());

        // План на сегодня зафиксирован
        var currentSettings = SettingsRepo.GetUserSettings();
        currentSettings.TodayPlanLockedDate.Should().Be(_today.ToDateTime());
    }

    [Fact]
    public void Postpone_WithReplacement_WhenDayWasAlreadyLocked_PreservesLockAndFillsOnlyFreedQuota()
    {
        // Arrange: День уже был зафиксирован
        var settings = new UserSettingsBuilder()
            .WithDailyTimeLimit(180)
            .WithTodayPlanLockedDate(_today.ToDateTime())
            .Build();
        SettingsRepo.UpdateSettings(settings);

        var taskA = new TaskItemBuilder()
            .WithId(101)
            .WithEstimatedMinutes(40)
            .WithScheduledDate(_today.ToDateTime(), DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var taskB = new TaskItemBuilder()
            .WithId(102)
            .WithEstimatedMinutes(40)
            .WithScheduledDate(_today.ToDateTime(), DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        // Кандидаты в бэклоге: TaskC (30 мин - влезает в квоту 40), TaskD (50 мин - не влезает)
        var taskC = new TaskItemBuilder()
            .WithId(103)
            .WithEstimatedMinutes(30)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var taskD = new TaskItemBuilder()
            .WithId(104)
            .WithEstimatedMinutes(50)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(taskA);
        TaskRepo.Add(taskB);
        TaskRepo.Add(taskC);
        TaskRepo.Add(taskD);

        // Act: Заменяем Задачу А (40 мин)
        PlannerService.PostponeTask(taskA.Id, PostponeTarget.Auto, replaceWithBacklog: true, settings);

        // Assert:
        var savedA = TaskRepo.GetById(taskA.Id);
        var savedC = TaskRepo.GetById(taskC.Id);
        var savedD = TaskRepo.GetById(taskD.Id);

        savedA!.ScheduledDate.Should().NotBe(_today.ToDateTime());
        savedC!.ScheduledDate.Should().Be(_today.ToDateTime()); // взята вместо А
        savedD!.ScheduledDate.Should().NotBe(_today.ToDateTime()); // не взята, лимит дня защищен

        // Замок дня сохранился
        var currentSettings = SettingsRepo.GetUserSettings();
        currentSettings.TodayPlanLockedDate.Should().Be(_today.ToDateTime());
    }

    [Fact]
    public void WeekDays_Recurrence_WhenOverdue_DoesNotScheduleOnInvalidDays()
    {
        // Arrange: Задача повторяется только по четвергам (маска 8: 1=Пн, 2=Вт, 4=Ср, 8=Чт)
        var thursdayMask = 8;
        var task = new TaskItemBuilder()
            .WithId(500)
            .WithTitle("Thursday Report")
            .WithRecurrence(RecurrenceType.WeekDays, weekDays: thursdayMask)
            .WithDateSource(DateSource.AutoFixed)
            .WithScheduledDate(_today.AddDays(-10).ToDateTime(), DateSource.AutoFixed) // просрочена на 10 дней
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);

        // Act: Выполняем нормализацию
        TaskRepo.NormalizeTaskDateSources();

        // Assert:
        var normalized = TaskRepo.GetById(task.Id);
        normalized.Should().NotBeNull();
        normalized!.ScheduledDate.Should().NotBeNull();

        // Дата обязана быть четвергом!
        normalized.ScheduledDate!.Value.DayOfWeek.Should().Be(DayOfWeek.Thursday);
        normalized.ScheduledDate.Value.Date.Should().BeOnOrAfter(_today.ToDateTime().Date);
    }

    [Fact]
    public void TaskDateNormalizer_Respects_SnoozedUntilDate_ForRecurringTask()
    {
        // Arrange: Повторяющаяся ежедневная задача отложена до завтра
        var task = new TaskItemBuilder()
            .WithId(600)
            .WithTitle("Daily Exercise")
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
            .WithDateSource(DateSource.AutoFixed)
            .WithScheduledDate(_today.Tomorrow.ToDateTime())
            .WithSnoozedUntilDate(_today.Tomorrow.ToDateTime())
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);

        // Act: Запускаем нормализатор
        TaskRepo.NormalizeTaskDateSources();

        // Assert: Нормализатор не стягивает задачу на сегодня
        var saved = TaskRepo.GetById(task.Id);
        saved!.ScheduledDate.Should().Be(_today.Tomorrow.ToDateTime());
    }

    [Fact]
    public void RecalculateAll_Clears_TodayPlanLockedDate()
    {
        // Arrange: День зафиксирован
        var settings = new UserSettingsBuilder()
            .WithTodayPlanLockedDate(_today.ToDateTime())
            .Build();
        SettingsRepo.UpdateSettings(settings);

        // Act: Пользователь нажимает "Пересчитать всё"
        PlannerService.RecalculateAll(settings);

        // Assert: Фиксация дня снята
        var currentSettings = SettingsRepo.GetUserSettings();
        currentSettings.TodayPlanLockedDate.Should().BeNull();
    }
}
