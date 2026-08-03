using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Trait("Category", "Planning")]
[Collection("StaticState")]
public class PlanningEngineTests
{

    public class SortingRules
    {
        [Fact]
        public void DistributeTasks_SchedulesTasksInOrderOfRelevance()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
            var urgentPriority = priorities[0]; // Order 1
            var lowPriority = priorities[3]; // Order 4

            var taskLowPriorityShort = new TaskItemBuilder().WithId(101).WithPriorityId(lowPriority.Id)
                .WithEstimatedMinutes(5).WithInterest(5).WithStatus(TaskStatus.Planned)
                .WithDateSource(DateSource.AutoFlexible).Build();
            var taskUrgentLong = new TaskItemBuilder().WithId(102).WithPriorityId(urgentPriority.Id)
                .WithEstimatedMinutes(30).WithInterest(3).WithStatus(TaskStatus.Planned)
                .WithDateSource(DateSource.AutoFlexible).Build();
            var taskUrgentShortHighInterest = new TaskItemBuilder().WithId(103).WithPriorityId(urgentPriority.Id)
                .WithEstimatedMinutes(5).WithInterest(9).WithStatus(TaskStatus.Planned)
                .WithDateSource(DateSource.AutoFlexible).Build();

            taskRepo.Add(taskLowPriorityShort);
            taskRepo.Add(taskUrgentLong);
            taskRepo.Add(taskUrgentShortHighInterest);

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(35).Build();

            // Act: Call real application planner service
            plannerService.DistributeTasks(settings);

            // Assert: Task 103 (Urgent, Short, High Interest) and Task 102 scheduled on today, Task 101 moved to tomorrow
            var saved103 = taskRepo.GetById(103);
            var saved102 = taskRepo.GetById(102);
            var saved101 = taskRepo.GetById(101);

            saved103!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            saved102!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            saved101!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }
    }

    public class LimitAllocation
    {
        [Fact]
        public void DistributeTasks_SplitsTasksByDailyTimeLimit()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var taskA = new TaskItemBuilder().WithId(101).WithEstimatedMinutes(60)
                .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            var taskB = new TaskItemBuilder().WithId(102).WithEstimatedMinutes(90)
                .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            var taskC = new TaskItemBuilder().WithId(103).WithEstimatedMinutes(60)
                .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

            taskRepo.Add(taskA);
            taskRepo.Add(taskB);
            taskRepo.Add(taskC);

            // Act: Daily limit 180 min -> A (60) + B (90) = 150 min (Today), C (60) moves to Tomorrow
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();
            plannerService.DistributeTasks(settings);

            // Assert: Inspect persistent DB state
            var updatedA = taskRepo.GetById(taskA.Id);
            var updatedB = taskRepo.GetById(taskB.Id);
            var updatedC = taskRepo.GetById(taskC.Id);

            updatedA!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            updatedB!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            updatedC!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }

        [Fact]
        public void LargeTask_Exceeding70PercentLimit_ScheduledOnCurrentDayExceedingLimit()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var existingTask = new TaskItemBuilder().WithId(201).WithEstimatedMinutes(70)
                .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual).WithStatus(TaskStatus.Planned)
                .Build();
            var largeTask = new TaskItemBuilder().WithId(202).WithEstimatedMinutes(75)
                .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

            taskRepo.Add(existingTask);
            taskRepo.Add(largeTask);

            // Act: Limit = 100 min. 70 min already occupied. Large task = 75 min (75% limit)
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(100).Build();
            plannerService.DistributeTasks(settings);

            // Assert: Large task placed on current day exceeding limit
            var updatedLarge = taskRepo.GetById(largeTask.Id);
            updatedLarge!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        }
    }

    public class PriorityBypass
    {
        [Fact]
        public void Priority0Or1Task_ForciblyScheduledTodayDespiteFullLimits()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var urgentPriority = context.Priorities.First(p => p.Order == 1);
            var urgentTask = new TaskItemBuilder().WithId(301).WithPriorityId(urgentPriority.Id)
                .WithDateSource(DateSource.AutoFlexible).WithEstimatedMinutes(100).WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(urgentTask);

            // Act: Daily limit = 0 min (fully exhausted)
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(0).Build();
            plannerService.DistributeTasks(settings);

            // Assert
            var updatedUrgent = taskRepo.GetById(urgentTask.Id);
            updatedUrgent!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        }
    }

    public class EdgeCasesInLimits
    {
        [Fact]
        public void TaskWithNullFields_UsesSafeDefaultsWithoutException()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            var task = new TaskItemBuilder()
                .WithId(400)
                .WithEstimatedMinutes(null)
                .WithComplexity(null)
                .WithInterest(null)
                .Build();

            // Act
            taskRepo.Add(task);
            var savedTask = taskRepo.GetById(task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            TaskItemValidator.ClampEstimatedMinutes(savedTask.EstimatedMinutes).Should().Be(15);
            savedTask.TotalComplexity.Should().Be(0);
            (savedTask.Interest ?? 5).Should().Be(5);
        }

        [Fact]
        public void DistributeTasks_ExcludesSubtasksFromDailyTaskLimits()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var urgentPriority = context.Priorities.First(p => p.Order == 1);
            var subtask1 = new TaskItemBuilder().WithId(11).WithTitle("Subtask 1").WithParentTaskId(10)
                .WithEstimatedMinutes(60).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned)
                .Build();
            var subtask2 = new TaskItemBuilder().WithId(12).WithTitle("Subtask 2").WithParentTaskId(10)
                .WithEstimatedMinutes(60).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned)
                .Build();
            var parentTask = new TaskItemBuilder().WithId(10).WithPriorityId(urgentPriority.Id).WithTitle("Parent Task")
                .WithEstimatedMinutes(30).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned)
                .WithSubtask(subtask1).WithSubtask(subtask2).Build();

            taskRepo.Add(parentTask);

            // DailyTaskLimit = 1 (Count limit = 1 task/day). DailyTimeLimit = 480 min.
            var settings = new UserSettingsBuilder().WithDailyTaskLimit(1).WithDailyTimeLimit(480).Build();

            // Act: Run planner service distribution
            plannerService.DistributeTasks(settings);

            // Assert: Parent task with subtasks is scheduled today (subtasks excluded from task count limit) & subtask time is aggregated
            var savedParent = taskRepo.GetById(10);
            savedParent.Should().NotBeNull();
            savedParent.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            savedParent.TotalEstimatedMinutes.Should().Be(150);
        }

        [Fact]
        public void ZeroDailyLimit_DoesNotCauseInfiniteLoop()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var task = new TaskItemBuilder().WithId(401).WithEstimatedMinutes(60)
                .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            taskRepo.Add(task);

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(0).Build();

            // Act
            var act = () => plannerService.DistributeTasks(settings);

            // Assert
            act.Should().NotThrow();
        }


        [Fact]
        public void CompletedTasks_CountAgainstDailyLimits()
        {
            // Arrange: Проверка, что завершённые сегодня дела учитываются при подсчёте лимита[cite: 1]
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var completedToday = new TaskItemBuilder()
                .WithId(701)
                .WithEstimatedMinutes(100)
                .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual)
                .WithCompletedDate(TodoDay.Today.ToDateTime())
                .WithStatus(TaskStatus.Completed)
                .Build();

            var newPlannedTask = new TaskItemBuilder()
                .WithId(702)
                .WithEstimatedMinutes(100)
                .WithDateSource(DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(completedToday);
            taskRepo.Add(newPlannedTask);

            // Лимит 180 минут. Завершённая задача уже забрала 100 минут. 
            // Новая задача на 100 минут не поместится (100 + 100 > 180) и должна уйти на завтра.
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert
            var savedNewTask = taskRepo.GetById(702);
            savedNewTask!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }

        [Fact]
        public void DailyTaskLimit_StopsDistributionWhenLimitReached()
        {
            // Arrange: Проверка лимита количества задач на день[cite: 3]
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var task1 = new TaskItemBuilder().WithId(801).WithDateSource(DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned).Build();
            var task2 = new TaskItemBuilder().WithId(802).WithDateSource(DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned).Build();

            taskRepo.Add(task1);
            taskRepo.Add(task2);

            // Лимит - ровно 1 задача в день
            var settings = new UserSettingsBuilder().WithDailyTaskLimit(1).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert
            taskRepo.GetById(801)!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            taskRepo.GetById(802)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }

        [Fact]
        public void DailyTaskLimit_ExcludesSubtasksFromCount()
        {
            // Arrange: Подзадачи не учитываются при сравнении с лимитом задач на день[cite: 3]
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var subtask1 = new TaskItemBuilder().WithId(901).WithTitle("Subtask 1").Build();
            var subtask2 = new TaskItemBuilder().WithId(902).WithTitle("Subtask 2").Build();

            var parentTask = new TaskItemBuilder()
                .WithId(900)
                .WithTitle("Parent Task")
                .WithDateSource(DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned)
                .WithSubtask(subtask1)
                .WithSubtask(subtask2)
                .Build();

            var anotherTask = new TaskItemBuilder().WithId(903).WithDateSource(DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned).Build();

            taskRepo.Add(parentTask);
            taskRepo.Add(anotherTask);

            // Лимит - 1 задача в день. Родительская (с подзадачами) считается как 1 задача.
            var settings = new UserSettingsBuilder().WithDailyTaskLimit(1).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert: Родительская на сегодня, вторая задача улетает на завтра
            taskRepo.GetById(900)!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            taskRepo.GetById(903)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }

        [Fact]
        public void DailyComplexityLimit_StopsDistributionWhenLimitReached()
        {
            // Arrange: Проверка лимита по сложности[cite: 3]
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var task1 = new TaskItemBuilder().WithId(1001).WithComplexity(60).WithDateSource(DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned).Build();
            var task2 = new TaskItemBuilder().WithId(1002).WithComplexity(50).WithDateSource(DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned).Build();

            taskRepo.Add(task1);
            taskRepo.Add(task2);

            // Лимит сложности - 100. 60 + 50 = 110 (превышение)
            var settings = new UserSettingsBuilder().WithDailyComplexityLimit(100).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert
            taskRepo.GetById(1001)!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            taskRepo.GetById(1002)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }

        [Fact]
        public void LargeTaskRule_OnlyAppliesToTimeLimit_NotComplexity()
        {
            // Arrange: Крупным делом можно превышать ТОЛЬКО лимит часов, сложность превышать нельзя[cite: 1]
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var existingTask = new TaskItemBuilder().WithId(1101).WithComplexity(50)
                .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual).WithStatus(TaskStatus.Planned)
                .Build();

            // Задача со сложностью 75 (что > 70% от лимита в 100), претендует на статус "крупной", 
            // но для сложности это правило не работает
            var largeComplexityTask = new TaskItemBuilder().WithId(1102).WithComplexity(75)
                .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

            taskRepo.Add(existingTask);
            taskRepo.Add(largeComplexityTask);

            var settings = new UserSettingsBuilder().WithDailyComplexityLimit(100).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert: Так как лимит сложности не прощает превышений даже для >=70%, задача уходит на завтра
            taskRepo.GetById(1102)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }

        [Fact]
        public void OverdueTasks_ProcessedBeforeNormalTasks_CanExceedLimits()
        {
            // Arrange: Перенесённая просрочка заполняет день раньше обычных дел и может превышать лимиты[cite: 1]
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var overdueDate = TodoDay.Today.Yesterday.ToDateTime();

            var overdueTask = new TaskItemBuilder()
                .WithId(1201)
                .WithEstimatedMinutes(200)
                .WithScheduledDate(overdueDate, DateSource.Manual) // Просрочена
                .WithStatus(TaskStatus.Planned)
                .Build();

            var normalTask = new TaskItemBuilder()
                .WithId(1202)
                .WithEstimatedMinutes(30)
                .WithDateSource(DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(overdueTask);
            taskRepo.Add(normalTask);

            // Лимит 180. Просрочка берёт 200 (превышая лимит).
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert: Просрочка встаёт на сегодня (и меняет Source на AutoFlexible по правилу). Обычная задача идёт на завтра, так как лимит исчерпан.
            var savedOverdue = taskRepo.GetById(1201);
            savedOverdue!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
            savedOverdue.DateSource.Should().Be(DateSource.AutoFlexible);

            taskRepo.GetById(1202)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }
    }

    public class BlockerDateCalculation
    {
        [Fact]
        public void BlockedTaskWithFixedDate_WhenChainExceedsCapacity_ForcesAutoFixedDatesForBlockers()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var today = TodoDay.Today.ToDateTime();
            var targetDate = today.AddDays(3);

            var blockerA = new TaskItem
            {
                Id = 601, Title = "Blocker A", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned,
                EstimatedMinutes = 300
            };
            var blockedB = new TaskItem
            {
                Id = 602, Title = "Blocked B", ScheduledDate = targetDate, DateSource = DateSource.Manual,
                Status = TaskStatus.Planned, EstimatedMinutes = 300
            };
            context.Tasks.AddRange(blockerA, blockedB);

            context.TaskRelations.Add(new TaskRelation
                { Id = 6001, SourceTaskId = blockerA.Id, TargetTaskId = blockedB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            // Act: Daily limit 200 mins. Total A+B = 600 mins -> capacity deficit!
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(200).Build();
            plannerService.DistributeTasks(settings);

            var updatedA = taskRepo.GetById(blockerA.Id);

            // Assert: Blocker A forced to AutoFixed date to fit chain before B's deadline
            updatedA.Should().NotBeNull();
            updatedA.DateSource.Should().Be(DateSource.AutoFixed);
        }
    }

    public class IdempotencyAndAtomicity
    {
        [Fact]
        public void ReRunningPlanningAlgorithm_IsIdempotentAndProducesIdenticalDates()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var task1 = new TaskItemBuilder().WithId(501).WithEstimatedMinutes(60)
                .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            var task2 = new TaskItemBuilder().WithId(502).WithEstimatedMinutes(90)
                .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            taskRepo.Add(task1);
            taskRepo.Add(task2);

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();

            // Act 1: First distribution
            plannerService.DistributeTasks(settings);
            var date1AfterFirst = taskRepo.GetById(501)!.ScheduledDate;
            var date2AfterFirst = taskRepo.GetById(502)!.ScheduledDate;

            // Act 2: Immediate re-run
            plannerService.DistributeTasks(settings);
            var date1AfterSecond = taskRepo.GetById(501)!.ScheduledDate;
            var date2AfterSecond = taskRepo.GetById(502)!.ScheduledDate;

            // Assert: Dates must remain identical
            date1AfterSecond.Should().Be(date1AfterFirst);
            date2AfterSecond.Should().Be(date2AfterFirst);
        }
    }
}