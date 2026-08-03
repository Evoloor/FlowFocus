using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Collection("StaticState")]
public class BlockingTaskTests
{
    [Fact]
    public void DistributeTasks_BlockedTaskDateIsNotEarlierThanBlockingTaskDate()
    {
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var urgentPriority = context.Priorities.OrderBy(p => p.Order).First();

        var taskA = new TaskItem
        {
            Id = 201, Title = "Blocking Task A", PriorityId = urgentPriority.Id, DateSource = DateSource.AutoFlexible,
            Status = TaskStatus.Planned, EstimatedMinutes = 300
        };
        var taskB = new TaskItem
        {
            Id = 202, Title = "Blocked Task B", PriorityId = urgentPriority.Id, DateSource = DateSource.AutoFlexible,
            Status = TaskStatus.Planned, EstimatedMinutes = 300
        };
        context.Tasks.AddRange(taskA, taskB);

        var relation = new TaskRelation
            { Id = 2001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(relation);
        context.SaveChanges();

        var notificationService = new NotificationService();
        var taskRepo = new TaskRepository(context, notificationService);
        var plannerService = new PlannerService(taskRepo);

        // Daily limit of 300 minutes means taskA and taskB cannot fit on the same day!
        var settings = new UserSettings { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };
        plannerService.DistributeTasks(settings);

        var updatedTaskA = taskRepo.GetById(taskA.Id);
        var updatedTaskB = taskRepo.GetById(taskB.Id);

        Assert.NotNull(updatedTaskA?.ScheduledDate);
        Assert.NotNull(updatedTaskB?.ScheduledDate);
        Assert.True(updatedTaskB.ScheduledDate >= updatedTaskA.ScheduledDate,
            "Blocked task scheduled date must be >= blocking task scheduled date");
    }

    [Fact]
    public void ApproachingDeadline_ConvertsBlockersToAutoFixedWhenBufferExhausted()
    {
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var today = TodoDay.Today.ToDateTime();
        var tomorrow = today.AddDays(1);

        var taskA = new TaskItem
        {
            Id = 301, Title = "Blocking Task A", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned,
            EstimatedMinutes = 300
        };
        var taskB = new TaskItem
        {
            Id = 302, Title = "Blocked Task B with Manual Date", ScheduledDate = tomorrow,
            DateSource = DateSource.Manual, Status = TaskStatus.Planned, EstimatedMinutes = 300
        };
        context.Tasks.AddRange(taskA, taskB);

        var relation = new TaskRelation
            { Id = 3001, SourceTaskId = taskA.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks };
        context.TaskRelations.Add(relation);
        context.SaveChanges();

        var notificationService = new NotificationService();
        var taskRepo = new TaskRepository(context, notificationService);
        var plannerService = new PlannerService(taskRepo);

        // Daily limit 300 mins. Total time A+B = 600 mins -> required days = 2.
        // Start date = tomorrow - 1 day = Today. Since start date <= Today, deadline is approaching!
        var settings = new UserSettings { DailyTimeLimit = 300, DailyComplexityLimit = 100, DailyTaskLimit = 10 };
        plannerService.DistributeTasks(settings);

        var updatedTaskA = taskRepo.GetById(taskA.Id);
        Assert.NotNull(updatedTaskA);
        Assert.Equal(DateSource.AutoFixed, updatedTaskA.DateSource);
        Assert.Equal(tomorrow, updatedTaskA.ScheduledDate);
    }
    
    [Fact]
    public void NotConfiguredTask_WhenSavedWithActiveBlocker_TransitionsToBlockedStatus()
    {
        // Arrange: Ненастроенная задача сохраняет статус при получении связи, но меняет его на Blocked при настройке
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

        var blocker = new TaskItemBuilder().WithId(201).WithStatus(TaskStatus.Planned).Build();
    
        // Задача изначально не настроена
        var notConfiguredTask = new TaskItemBuilder().WithId(202).WithStatus(TaskStatus.NotConfigured).Build();
    
        context.Tasks.AddRange(blocker, notConfiguredTask);
        context.TaskRelations.Add(new TaskRelation { SourceTaskId = blocker.Id, TargetTaskId = notConfiguredTask.Id, Type = RelationType.Blocks });
        context.SaveChanges();

        // Act 1: Проверяем, что связь не изменила статус автоматически
        var taskBeforeEdit = taskRepo.GetById(notConfiguredTask.Id);
        taskBeforeEdit!.Status.Should().Be(TaskStatus.NotConfigured);

        // Act 2: Пользователь открыл диалог, настроил задачу и сохранил (стандартный флоу переводит в Planned)
        taskBeforeEdit.Title = "Настроенное название";
        taskBeforeEdit.Status = TaskStatus.Planned; // Имитация прихода DTO с формы
    
        taskRepo.Update(taskBeforeEdit);

        // Assert: Репозиторий/Сервис должен перехватить сохранение и форсировать статус Blocked
        var savedTask = taskRepo.GetById(notConfiguredTask.Id);
        savedTask!.Status.Should().Be(TaskStatus.Blocked);
    }
    
    [Fact]
    public void AutoEscalation_OfBlockedTask_ForcesCascadePriorityIncreaseOnBlockers()
    {
        // Arrange: Форсирование приоритета блокеров при авто-повышении заблокированной
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
        var plannerService = new PlannerService(taskRepo);

        var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
        var critical = priorities[0];
        var low = priorities[3];

        // Блокер имеет низкий приоритет
        var blocker = new TaskItemBuilder().WithId(501).WithPriorityId(low.Id).WithStatus(TaskStatus.Planned).Build();
    
        // Заблокированная задача тоже имеет низкий, но у неё есть правило повышения до критического сегодня
        var blocked = new TaskItemBuilder().WithId(502).WithPriorityId(low.Id).WithStatus(TaskStatus.Planned).Build();
        blocked.PriorityEscalations.Add(new PriorityEscalation { TargetPriorityId = critical.Id, EscalationDate = TodoDay.Today.ToDateTime() });

        context.Tasks.AddRange(blocker, blocked);
        context.TaskRelations.Add(new TaskRelation { SourceTaskId = blocker.Id, TargetTaskId = blocked.Id, Type = RelationType.Blocks });
        context.SaveChanges();

        // Act: Запускаем цикл актуализации приоритетов (который должен включать нормализацию связей)
        plannerService.ActualizePriorities();
        // TODO: ИИ хотела написать: plannerService.NormalizeBlockerPriorities(); // Каскадное форсирование
        taskRepo.SaveChanges();

        // Assert: Блокер должен подтянуться до критического приоритета вслед за заблокированной
        var updatedBlocker = taskRepo.GetById(blocker.Id);
        var updatedBlocked = taskRepo.GetById(blocked.Id);

        updatedBlocked!.PriorityId.Should().Be(critical.Id);
        updatedBlocker!.PriorityId.Should().Be(critical.Id);
    }
}

[Collection("StaticState")]
public class BlockerDistributionOrder
{
    [Fact]
    public void DistributeTasks_AlwaysSchedulesBlockerBeforeOrOnSameDayAsBlockedTask()
    {
        // Arrange: Задача B заблокирована задачей A. 
        // У задачи B параметры сортировки (Интересность) максимально высокие, у A - минимальные.
        using var
            context = TestDbContextFactory
                .CreateInMemoryContext(); // Используем вашу новую фабрику TestDbContextFactory
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
        var plannerService = new PlannerService(taskRepo);

        var taskA_Blocker = new TaskItemBuilder()
            .WithId(601)
            .WithTitle("Blocker A")
            .WithInterest(1) // Низкая привлекательность
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var taskB_Blocked = new TaskItemBuilder()
            .WithId(602)
            .WithTitle("Blocked B")
            .WithInterest(10) // Высокая привлекательность, в обычной сортировке встала бы первой
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        context.Tasks.AddRange(taskA_Blocker, taskB_Blocked);

        // A блокирует B
        context.TaskRelations.Add(new TaskRelation
            { SourceTaskId = taskA_Blocker.Id, TargetTaskId = taskB_Blocked.Id, Type = RelationType.Blocks });
        context.SaveChanges();

        // Лимит времени сделаем маленьким (например, 60 минут), чтобы задачи точно разнесло по разным дням, 
        // при условии что каждая занимает по 45 минут.
        taskA_Blocker.EstimatedMinutes = 45;
        taskB_Blocked.EstimatedMinutes = 45;

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(60).Build();

        // Act: Запускаем алгоритм перераспределения
        plannerService.DistributeTasks(settings);

        // Assert: Фактическая дата заблокированной задачи (B) НИКОГДА не должна быть раньше даты блокера (A)
        var updatedA = taskRepo.GetById(taskA_Blocker.Id);
        var updatedB = taskRepo.GetById(taskB_Blocked.Id);

        updatedA.Should().NotBeNull();
        updatedB.Should().NotBeNull();

        // Дата B должна быть больше или равна дате A
        updatedB!.ScheduledDate.Should().BeOnOrAfter(updatedA!.ScheduledDate.Value);
    }
}

[Collection("StaticState")]
public class BlockedTaskCompletion
{
    [Fact]
    public void CompleteOrMarkIrrelevantBlockedTask_RemovesIncomingBlockerRelations()
    {
        // Arrange: Задача B заблокирована задачами A1 и A2
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

        var taskA1 = new TaskItem { Id = 701, Title = "Blocker A1", Status = TaskStatus.Planned };
        var taskA2 = new TaskItem { Id = 702, Title = "Blocker A2", Status = TaskStatus.Planned };
        var taskB = new TaskItem { Id = 703, Title = "Blocked Task B", Status = TaskStatus.Planned };

        context.Tasks.AddRange(taskA1, taskA2, taskB);
        context.TaskRelations.Add(new TaskRelation
            { Id = 7001, SourceTaskId = taskA1.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        context.TaskRelations.Add(new TaskRelation
            { Id = 7002, SourceTaskId = taskA2.Id, TargetTaskId = taskB.Id, Type = RelationType.Blocks });
        context.SaveChanges();

        // Act: Принудительно завершаем заблокированную задачу B
        // В реальном UI перед этим будет ивент/модалка onbeforecomplete, но репозиторий должен уметь чистить связи
        taskRepo.CompleteTask(taskB.Id);

        // Assert: Входящие связи блокировки для задачи B должны быть удалены
        var savedB = taskRepo.GetById(taskB.Id);
        var remainingRelationsToB = context.TaskRelations
            .Where(r => r.TargetTaskId == taskB.Id && r.Type == RelationType.Blocks)
            .ToList();

        savedB.Should().NotBeNull();
        savedB!.Status.Should().Be(TaskStatus.Completed);
        remainingRelationsToB.Should()
            .BeEmpty("Каскадный статус блокера должен быть снят при завершении заблокированной задачи");
    }
}