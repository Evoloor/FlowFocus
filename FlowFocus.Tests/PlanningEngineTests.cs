using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for planning engine algorithms, distribution rules, limits, and idempotency.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Planning")]
[Collection(name: "StaticState")]
public class PlanningEngineTests
{
    /// <summary>
    /// Tests verification of relevance sorting rules in task distribution.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Planning")]
    public class SortingRules
    {
        /// <summary>
        /// Verifies that DistributeTasks schedules tasks in priority and relevance order.
        /// </summary>
        [Fact]
        public void DistributeTasks_SchedulesTasksInOrderOfRelevance()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var priorities = context.Priorities.OrderBy(keySelector: p => p.Order).ToList();
            var urgentPriority = priorities[index: 0]; // Order 1
            var lowPriority = priorities[index: 3]; // Order 4

            var taskLowPriorityShort = new TaskItemBuilder().WithId(id: 101).WithPriorityId(priorityId: lowPriority.Id)
                .WithEstimatedMinutes(minutes: 5).WithInterest(interest: 5).WithStatus(status: TaskStatus.Planned)
                .WithDateSource(source: DateSource.AutoFlexible).Build();
            var taskUrgentLong = new TaskItemBuilder().WithId(id: 102).WithPriorityId(priorityId: urgentPriority.Id)
                .WithEstimatedMinutes(minutes: 30).WithInterest(interest: 3).WithStatus(status: TaskStatus.Planned)
                .WithDateSource(source: DateSource.AutoFlexible).Build();
            var taskUrgentShortHighInterest = new TaskItemBuilder().WithId(id: 103)
                .WithPriorityId(priorityId: urgentPriority.Id)
                .WithEstimatedMinutes(minutes: 5).WithInterest(interest: 9).WithStatus(status: TaskStatus.Planned)
                .WithDateSource(source: DateSource.AutoFlexible).Build();

            taskRepo.Add(entity: taskLowPriorityShort);
            taskRepo.Add(entity: taskUrgentLong);
            taskRepo.Add(entity: taskUrgentShortHighInterest);

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 35).Build();

            // Act: Call real application planner service
            plannerService.DistributeTasks(settings: settings);

            // Assert: Task 103 (Urgent, Short, High Interest) and Task 102 scheduled today, Task 101 moved to tomorrow
            var saved103 = taskRepo.GetById(id: 103);
            var saved102 = taskRepo.GetById(id: 102);
            var saved101 = taskRepo.GetById(id: 101);

            saved103!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            saved102!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            saved101!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }
    }

    /// <summary>
    /// Tests verification of daily time limit allocation and large task handling.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Planning")]
    public class LimitAllocation
    {
        /// <summary>
        /// Verifies that DistributeTasks splits tasks according to daily time limit.
        /// </summary>
        [Fact]
        public void DistributeTasks_SplitsTasksByDailyTimeLimit()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var taskA = new TaskItemBuilder().WithId(id: 101).WithEstimatedMinutes(minutes: 60)
                .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();
            var taskB = new TaskItemBuilder().WithId(id: 102).WithEstimatedMinutes(minutes: 90)
                .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();
            var taskC = new TaskItemBuilder().WithId(id: 103).WithEstimatedMinutes(minutes: 60)
                .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();

            taskRepo.Add(entity: taskA);
            taskRepo.Add(entity: taskB);
            taskRepo.Add(entity: taskC);

            // Act: Daily limit 180 min -> A (60) + B (90) = 150 min (Today), C (60) moves to Tomorrow
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 180).Build();
            plannerService.DistributeTasks(settings: settings);

            // Assert: Inspect persistent DB state
            var updatedA = taskRepo.GetById(id: taskA.Id);
            var updatedB = taskRepo.GetById(id: taskB.Id);
            var updatedC = taskRepo.GetById(id: taskC.Id);

            updatedA!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            updatedB!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            updatedC!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }

        /// <summary>
        /// Verifies that a large task exceeding 70% of daily limit is scheduled on current day exceeding limit.
        /// </summary>
        [Fact]
        public void LargeTask_Exceeding70PercentLimit_ScheduledOnCurrentDayExceedingLimit()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var existingTask = new TaskItemBuilder().WithId(id: 201).WithEstimatedMinutes(minutes: 70)
                .WithScheduledDate(date: TodoDay.Today.ToDateTime(), dateSource: DateSource.Manual)
                .WithStatus(status: TaskStatus.Planned)
                .Build();
            var largeTask = new TaskItemBuilder().WithId(id: 202).WithEstimatedMinutes(minutes: 75)
                .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();

            taskRepo.Add(entity: existingTask);
            taskRepo.Add(entity: largeTask);

            // Act: Limit = 100 min. 70 min already occupied. Large task = 75 min (75% limit)
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 100).Build();
            plannerService.DistributeTasks(settings: settings);

            // Assert: Large task placed on current day exceeding limit
            var updatedLarge = taskRepo.GetById(id: largeTask.Id);
            updatedLarge!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
        }
    }

    /// <summary>
    /// Tests verification of urgent priority bypass rules.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Planning")]
    public class PriorityBypass
    {
        /// <summary>
        /// Verifies that urgent (Priority 0 or 1) tasks are forcibly scheduled today despite full daily limits.
        /// </summary>
        [Fact]
        public void Priority0Or1Task_ForciblyScheduledTodayDespiteFullLimits()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var urgentPriority = context.Priorities.First(predicate: p => p.Order == 1);
            var urgentTask = new TaskItemBuilder().WithId(id: 301).WithPriorityId(priorityId: urgentPriority.Id)
                .WithDateSource(source: DateSource.AutoFlexible).WithEstimatedMinutes(minutes: 100)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            taskRepo.Add(entity: urgentTask);

            // Act: Daily limit = 0 min (fully exhausted)
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 0).Build();
            plannerService.DistributeTasks(settings: settings);

            // Assert
            var updatedUrgent = taskRepo.GetById(id: urgentTask.Id);
            updatedUrgent!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
        }
    }

    /// <summary>
    /// Tests verification of edge cases in daily limits and default properties.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Planning")]
    public class EdgeCasesInLimits
    {
        /// <summary>
        /// Verifies that tasks with null fields utilize safe domain validator defaults.
        /// </summary>
        [Fact]
        public void TaskWithNullFields_UsesSafeDefaultsWithoutException()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());

            var task = new TaskItemBuilder()
                .WithId(id: 400)
                .WithEstimatedMinutes(minutes: null)
                .WithComplexity(complexity: null)
                .WithInterest(interest: null)
                .Build();

            // Act
            taskRepo.Add(entity: task);
            var savedTask = taskRepo.GetById(id: task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            TaskItemValidator.ClampEstimatedMinutes(minutes: savedTask.EstimatedMinutes).Should().Be(expected: 15);
            savedTask.TotalComplexity.Should().Be(expected: 0);
            (savedTask.Interest ?? 5).Should().Be(expected: 5);
        }

        /// <summary>
        /// Verifies that subtasks are excluded from daily task count limits and subtask duration is aggregated.
        /// </summary>
        [Fact]
        public void DistributeTasks_ExcludesSubtasksFromDailyTaskLimits()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var urgentPriority = context.Priorities.First(predicate: p => p.Order == 1);
            var subtask1 = new TaskItemBuilder().WithId(id: 11).WithTitle(title: "Subtask 1")
                .WithParentTaskId(parentId: 10)
                .WithEstimatedMinutes(minutes: 60).WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .Build();
            var subtask2 = new TaskItemBuilder().WithId(id: 12).WithTitle(title: "Subtask 2")
                .WithParentTaskId(parentId: 10)
                .WithEstimatedMinutes(minutes: 60).WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .Build();
            var parentTask = new TaskItemBuilder().WithId(id: 10).WithPriorityId(priorityId: urgentPriority.Id)
                .WithTitle(title: "Parent Task")
                .WithEstimatedMinutes(minutes: 30).WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .WithSubtask(subtask: subtask1).WithSubtask(subtask: subtask2).Build();

            taskRepo.Add(entity: parentTask);

            // DailyTaskLimit = 1 (Count limit = 1 task/day). DailyTimeLimit = 480 min.
            var settings = new UserSettingsBuilder().WithDailyTaskLimit(limit: 1).WithDailyTimeLimit(limit: 480)
                .Build();

            // Act: Run planner service distribution
            plannerService.DistributeTasks(settings: settings);

            // Assert: Parent task with subtasks is scheduled today (subtasks excluded from task count limit) & subtask time is aggregated
            var savedParent = taskRepo.GetById(id: 10);
            savedParent.Should().NotBeNull();
            savedParent.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            savedParent.TotalEstimatedMinutes.Should().Be(expected: 150);
        }

        /// <summary>
        /// Verifies that a zero daily time limit executes safely without infinite loops.
        /// </summary>
        [Fact]
        public void ZeroDailyLimit_DoesNotCauseInfiniteLoop()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var task = new TaskItemBuilder().WithId(id: 401).WithEstimatedMinutes(minutes: 60)
                .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();
            taskRepo.Add(entity: task);

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 0).Build();

            // Act
            var act = () => plannerService.DistributeTasks(settings: settings);

            // Assert
            act.Should().NotThrow();
        }

        /// <summary>
        /// Verifies that completed tasks today consume daily time limits for planning new tasks.
        /// </summary>
        [Fact]
        public void CompletedTasks_CountAgainstDailyLimits()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var completedToday = new TaskItemBuilder()
                .WithId(id: 701)
                .WithEstimatedMinutes(minutes: 100)
                .WithScheduledDate(date: TodoDay.Today.ToDateTime(), dateSource: DateSource.Manual)
                .WithCompletedDate(date: TodoDay.Today.ToDateTime())
                .WithStatus(status: TaskStatus.Completed)
                .Build();

            var newPlannedTask = new TaskItemBuilder()
                .WithId(id: 702)
                .WithEstimatedMinutes(minutes: 100)
                .WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            taskRepo.Add(entity: completedToday);
            taskRepo.Add(entity: newPlannedTask);

            // Limit 180 min. Completed task consumes 100 min.
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 180).Build();

            // Act
            plannerService.DistributeTasks(settings: settings);

            // Assert
            var savedNewTask = taskRepo.GetById(id: 702);
            savedNewTask!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }

        /// <summary>
        /// Verifies that daily task count limit stops distribution when limit is reached.
        /// </summary>
        [Fact]
        public void DailyTaskLimit_StopsDistributionWhenLimitReached()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var task1 = new TaskItemBuilder().WithId(id: 801).WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned).Build();
            var task2 = new TaskItemBuilder().WithId(id: 802).WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned).Build();

            taskRepo.Add(entity: task1);
            taskRepo.Add(entity: task2);

            // Limit = 1 task per day
            var settings = new UserSettingsBuilder().WithDailyTaskLimit(limit: 1).Build();

            // Act
            plannerService.DistributeTasks(settings: settings);

            // Assert
            taskRepo.GetById(id: 801)!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            taskRepo.GetById(id: 802)!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }

        /// <summary>
        /// Verifies that daily task count limit excludes subtasks from counting against limit.
        /// </summary>
        [Fact]
        public void DailyTaskLimit_ExcludesSubtasksFromCount()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var subtask1 = new TaskItemBuilder().WithId(id: 901).WithTitle(title: "Subtask 1").Build();
            var subtask2 = new TaskItemBuilder().WithId(id: 902).WithTitle(title: "Subtask 2").Build();

            var parentTask = new TaskItemBuilder()
                .WithId(id: 900)
                .WithTitle(title: "Parent Task")
                .WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .WithSubtask(subtask: subtask1)
                .WithSubtask(subtask: subtask2)
                .Build();

            var anotherTask = new TaskItemBuilder().WithId(id: 903).WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned).Build();

            taskRepo.Add(entity: parentTask);
            taskRepo.Add(entity: anotherTask);

            // Limit = 1 task per day
            var settings = new UserSettingsBuilder().WithDailyTaskLimit(limit: 1).Build();

            // Act
            plannerService.DistributeTasks(settings: settings);

            // Assert: Parent scheduled today, second task pushed to tomorrow
            taskRepo.GetById(id: 900)!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            taskRepo.GetById(id: 903)!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }

        /// <summary>
        /// Verifies that daily complexity limit stops distribution when limit is reached.
        /// </summary>
        [Fact]
        public void DailyComplexityLimit_StopsDistributionWhenLimitReached()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var task1 = new TaskItemBuilder().WithId(id: 1001).WithComplexity(complexity: 60)
                .WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned).Build();
            var task2 = new TaskItemBuilder().WithId(id: 1002).WithComplexity(complexity: 50)
                .WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned).Build();

            taskRepo.Add(entity: task1);
            taskRepo.Add(entity: task2);

            // Complexity limit = 100
            var settings = new UserSettingsBuilder().WithDailyComplexityLimit(limit: 100).Build();

            // Act
            plannerService.DistributeTasks(settings: settings);

            // Assert
            taskRepo.GetById(id: 1001)!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            taskRepo.GetById(id: 1002)!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }

        /// <summary>
        /// Verifies that large task rule applies strictly to time limit and not to complexity.
        /// </summary>
        [Fact]
        public void LargeTaskRule_OnlyAppliesToTimeLimit_NotComplexity()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var existingTask = new TaskItemBuilder().WithId(id: 1101).WithComplexity(complexity: 50)
                .WithScheduledDate(date: TodoDay.Today.ToDateTime(), dateSource: DateSource.Manual)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            var largeComplexityTask = new TaskItemBuilder().WithId(id: 1102).WithComplexity(complexity: 75)
                .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();

            taskRepo.Add(entity: existingTask);
            taskRepo.Add(entity: largeComplexityTask);

            var settings = new UserSettingsBuilder().WithDailyComplexityLimit(limit: 100).Build();

            // Act
            plannerService.DistributeTasks(settings: settings);

            // Assert
            taskRepo.GetById(id: 1102)!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }

        /// <summary>
        /// Verifies that overdue tasks are processed before normal tasks during planning.
        /// </summary>
        [Fact]
        public void OverdueTasks_ProcessedBeforeNormalTasks_CanExceedLimits()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var overdueDate = TodoDay.Today.Yesterday.ToDateTime();

            var overdueTask = new TaskItemBuilder()
                .WithId(id: 1201)
                .WithEstimatedMinutes(minutes: 200)
                .WithScheduledDate(date: overdueDate, dateSource: DateSource.Manual)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            var normalTask = new TaskItemBuilder()
                .WithId(id: 1202)
                .WithEstimatedMinutes(minutes: 30)
                .WithDateSource(source: DateSource.AutoFlexible)
                .WithStatus(status: TaskStatus.Planned)
                .Build();

            taskRepo.Add(entity: overdueTask);
            taskRepo.Add(entity: normalTask);

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 180).Build();

            // Act
            plannerService.DistributeTasks(settings: settings);

            // Assert
            var savedOverdue = taskRepo.GetById(id: 1201);
            savedOverdue!.ScheduledDate.Should().Be(expected: TodoDay.Today.ToDateTime());
            savedOverdue.DateSource.Should().Be(expected: DateSource.AutoFlexible);

            taskRepo.GetById(id: 1202)!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
        }
    }

    /// <summary>
    /// Tests verification of blocker date calculations during capacity deficits.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Planning")]
    public class BlockerDateCalculation
    {
        /// <summary>
        /// Verifies that blocked tasks with fixed dates force AutoFixed dates for blockers when chain exceeds capacity.
        /// </summary>
        [Fact]
        public void BlockedTaskWithFixedDate_WhenChainExceedsCapacity_ForcesAutoFixedDatesForBlockers()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var today = TodoDay.Today.ToDateTime();
            var targetDate = today.AddDays(value: 3);

            TaskItem blockerA = new()
            {
                Id = 601, Title = "Blocker A", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned,
                EstimatedMinutes = 300
            };
            TaskItem blockedB = new()
            {
                Id = 602, Title = "Blocked B", ScheduledDate = targetDate, DateSource = DateSource.Manual,
                Status = TaskStatus.Planned, EstimatedMinutes = 300
            };
            context.Tasks.AddRange(entities: [blockerA, blockedB]);

            context.TaskRelations.Add(entity: new()
                { Id = 6001, SourceTaskId = blockerA.Id, TargetTaskId = blockedB.Id, Type = RelationType.Blocks });
            context.SaveChanges();

            // Act: Daily limit 200 mins. Total A+B = 600 mins -> capacity deficit!
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 200).Build();
            plannerService.DistributeTasks(settings: settings);

            var updatedA = taskRepo.GetById(id: blockerA.Id);

            // Assert: Blocker A forced to AutoFixed date to fit chain before B's deadline
            updatedA.Should().NotBeNull();
            updatedA.DateSource.Should().Be(expected: DateSource.AutoFixed);
        }
    }

    /// <summary>
    /// Tests verification of planning algorithm idempotency.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Planning")]
    public class ImmutabilityTests
    {
        [Fact]
        public void InactiveTasks_AreStrictlyIgnoredByPlanner_AndNeverMoved()
        {
            // Arrange: Дела со статусами завершен или неактивен исключаются из распределения по дням[cite: 3]
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var yesterday = TodoDay.Today.Yesterday.ToDateTime();
            var today = TodoDay.Today.ToDateTime();

            // 1. Завершенная задача (например, просроченная и закрытая вчера)
            var completedTask = new TaskItemBuilder()
                .WithId(2001)
                .WithTitle("Старая завершенная")
                .WithEstimatedMinutes(100)
                .WithScheduledDate(yesterday, DateSource.Manual)
                .WithStatus(TaskStatus.Completed)
                .Build();

            // 2. Неактуальная задача (отмененная сегодня)
            var irrelevantTask = new TaskItemBuilder()
                .WithId(2002)
                .WithTitle("Отмененная сегодня")
                .WithEstimatedMinutes(100)
                .WithScheduledDate(today, DateSource.AutoFlexible)
                .WithStatus(TaskStatus.Irrelevant)
                .Build();

            taskRepo.Add(completedTask);
            taskRepo.Add(irrelevantTask);

            // Искусственно ставим очень жесткий лимит времени (50 минут).
            // Если планировщик случайно захватит неактивные задачи (у которых по 100 минут), 
            // он попытается их перенести на будущие дни из-за нехватки места.
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(50).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert: Планировщик не должен трогать даты и источники неактивных задач
            var savedCompleted = taskRepo.GetById(2001);
            var savedIrrelevant = taskRepo.GetById(2002);

            savedCompleted!.ScheduledDate.Should().Be(yesterday, "Дата завершенной задачи не должна меняться");
            savedCompleted.DateSource.Should().Be(DateSource.Manual, "Источник даты не должен сбрасываться");

            savedIrrelevant!.ScheduledDate.Should().Be(today, "Дата неактуальной задачи не должна меняться");
            savedIrrelevant.DateSource.Should().Be(DateSource.AutoFlexible);
        }

        [Fact]
        public void FutureManualTasks_AreNotPulledToToday_EvenIfLimitsAllow()
        {
            // Arrange: Планировщик не должен трогать будущие фиксированные даты, даже если сегодня куча свободного времени
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var tomorrow = TodoDay.Today.Tomorrow.ToDateTime();

            var futureManualTask = new TaskItemBuilder()
                .WithId(3001)
                .WithScheduledDate(tomorrow, DateSource.Manual) // Назначено на завтра вручную
                .WithEstimatedMinutes(30)
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(futureManualTask);

            // Лимит на сегодня огромный (480 минут), и сегодня вообще нет дел
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(480).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert: Задача должна остаться на завтрашнем дне. Источник не должен меняться.
            var savedTask = taskRepo.GetById(3001);
            savedTask!.ScheduledDate.Should().Be(tomorrow, "Будущие мануальные задачи нельзя сдвигать на сегодня");
            savedTask.DateSource.Should().Be(DateSource.Manual);
        }

        [Fact]
        public void RecurringTasks_IgnoreDailyLimits_AndAreNeverMovedByPlanner()
        {
            // Arrange: Повторяющиеся задачи не подлежат авто-перераспределению при нехватке лимитов
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var today = TodoDay.Today.ToDateTime();

            // Обычная задача, забивающая весь лимит
            var blockingTask = new TaskItemBuilder()
                .WithId(3002).WithDateSource(DateSource.AutoFlexible).WithEstimatedMinutes(180)
                .WithStatus(TaskStatus.Planned).Build();

            // Ежедневная задача, которая должна выполниться сегодня
            var recurringTask = new TaskItemBuilder()
                .WithId(3003).WithRecurrence(RecurrenceType.Daily).WithScheduledDate(today, DateSource.AutoFixed)
                .WithEstimatedMinutes(60).WithStatus(TaskStatus.Planned).Build();

            taskRepo.Add(blockingTask);
            taskRepo.Add(recurringTask);

            // Лимит всего 180. Обычная задача его уже съела.
            var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();

            // Act
            plannerService.DistributeTasks(settings);

            // Assert: Повторяющаяся задача ОБЯЗАНА остаться на сегодня
            var savedRecurring = taskRepo.GetById(3003);
            savedRecurring!.ScheduledDate.Should()
                .Be(today, "Повторяющиеся задачи не должны переноситься из-за лимитов");
        }

        [Fact]
        public void PriorityEscalation_DoesNotTrigger_BeforeEscalationDate()
        {
            // Arrange: Приоритет не должен повышаться, если дата маппинга еще не наступила
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
            var targetPriority = priorities[0]; // Критический
            var currentPriority = priorities[3]; // Низкий

            var tomorrow = TodoDay.Today.Tomorrow.ToDateTime();

            var task = new TaskItemBuilder()
                .WithId(4001).WithPriorityId(currentPriority.Id).WithStatus(TaskStatus.Planned).Build();

            // Повышение должно произойти только завтра
            task.PriorityEscalations.Add(new PriorityEscalation
                { TargetPriorityId = targetPriority.Id, EscalationDate = tomorrow });
            taskRepo.Add(task);
            context.SaveChanges();

            // Act
            plannerService.ActualizePriorities();
            taskRepo.SaveChanges();

            // Assert: Приоритет должен остаться прежним
            var savedTask = taskRepo.GetById(4001);
            savedTask!.PriorityId.Should().Be(currentPriority.Id, "Приоритет не должен повышаться раньше времени");
        }

        [Fact]
        public void StandardTaskCompletion_DoesNotTrigger_RecurrenceEngine()
        {
            // Arrange: Обычная (не повторяющаяся) задача не должна создавать копий при завершении
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            var standardTask = new TaskItemBuilder()
                .WithId(5001)
                .WithRecurrence(RecurrenceType.None) // Явно обычная задача
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(standardTask);

            // Act
            taskRepo.CompleteTask(standardTask.Id);

            // Assert
            var savedTask = taskRepo.GetById(5001);
            var allTasks = taskRepo.GetAll();

            savedTask!.Status.Should().Be(TaskStatus.Completed);

            // Убеждаемся, что в базе не появилось задачи, ссылающейся на эту как на источник повторения
            var copies = allTasks.Where(t => t.RecurrenceSourceId == standardTask.Id).ToList();
            copies.Should().BeEmpty("Завершение обычной задачи не должно создавать никаких копий");
        }
    }

    [UsedImplicitly]
    [Trait(name: "Category", value: "Planning")]
    public class IdempotencyAndAtomicity
    {
        /// <summary>
        /// Verifies that re-running planning algorithm produces identical dates.
        /// </summary>
        [Fact]
        public void ReRunningPlanningAlgorithm_IsIdempotentAndProducesIdenticalDates()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context,
                notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            var task1 = new TaskItemBuilder().WithId(id: 501).WithEstimatedMinutes(minutes: 60)
                .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();
            var task2 = new TaskItemBuilder().WithId(id: 502).WithEstimatedMinutes(minutes: 90)
                .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();
            taskRepo.Add(entity: task1);
            taskRepo.Add(entity: task2);

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 180).Build();

            // Act 1: First distribution
            plannerService.DistributeTasks(settings: settings);
            var date1AfterFirst = taskRepo.GetById(id: 501)!.ScheduledDate;
            var date2AfterFirst = taskRepo.GetById(id: 502)!.ScheduledDate;

            // Act 2: Immediate re-run
            plannerService.DistributeTasks(settings: settings);
            var date1AfterSecond = taskRepo.GetById(id: 501)!.ScheduledDate;
            var date2AfterSecond = taskRepo.GetById(id: 502)!.ScheduledDate;

            // Assert: Dates must remain identical
            date1AfterSecond.Should().Be(expected: date1AfterFirst);
            date2AfterSecond.Should().Be(expected: date2AfterFirst);
        }
    }

    /// <summary>
    /// Verifies that inactive tasks completed or marked irrelevant today consume daily limits for auto-flexible tasks.
    /// </summary>
    [Fact]
    public void InactiveTasks_CompletedOrIrrelevantToday_ConsumeDailyLimitsForAutoFlexible()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryContext();
        TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
        PlannerService plannerService = new(taskRepository: taskRepo);

        var completedToday = new TaskItemBuilder()
            .WithId(id: 101).WithEstimatedMinutes(minutes: 60)
            .WithScheduledDate(date: TodoDay.Today.ToDateTime(), dateSource: DateSource.Manual)
            .WithCompletedDate(date: TodoDay.Today.ToDateTime()).WithStatus(status: TaskStatus.Completed).Build();

        var irrelevantToday = new TaskItemBuilder()
            .WithId(id: 102).WithEstimatedMinutes(minutes: 60)
            .WithScheduledDate(date: TodoDay.Today.ToDateTime(), dateSource: DateSource.Manual)
            .WithCompletedDate(date: TodoDay.Today.ToDateTime()).WithStatus(status: TaskStatus.Irrelevant).Build();

        var newFlexibleTask = new TaskItemBuilder()
            .WithId(id: 103).WithEstimatedMinutes(minutes: 60)
            .WithDateSource(source: DateSource.AutoFlexible).WithStatus(status: TaskStatus.Planned).Build();

        taskRepo.Add(entity: completedToday);
        taskRepo.Add(entity: irrelevantToday);
        taskRepo.Add(entity: newFlexibleTask);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(limit: 150).Build();

        // Act
        plannerService.DistributeTasks(settings: settings);

        // Assert
        var savedNewTask = taskRepo.GetById(id: 103);
        savedNewTask!.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that completing a severely overdue recurring task skips to next actual future date.
    /// </summary>
    [Fact]
    public void SeverelyOverdueRecurringTask_WhenCompleted_SkipsToNextActualFutureDate()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryContext();
        TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

        var tenDaysAgo = TodoDay.Today.ToDateTime().AddDays(value: -10);
        var overdueTask = new TaskItemBuilder()
            .WithId(id: 301).WithRecurrence(type: RecurrenceType.Daily)
            .WithScheduledDate(date: tenDaysAgo, dateSource: DateSource.AutoFixed)
            .WithStatus(status: TaskStatus.Planned).Build();

        taskRepo.Add(entity: overdueTask);

        // Act
        taskRepo.CompleteTask(taskId: overdueTask.Id);

        // Assert
        var newCopy = taskRepo.GetAll().FirstOrDefault(predicate: t => t.RecurrenceSourceId == overdueTask.Id);
        newCopy.Should().NotBeNull();
        newCopy.ScheduledDate.Should().Be(expected: TodoDay.Today.Tomorrow.ToDateTime());
    }
}