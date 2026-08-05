using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for planning engine algorithms, distribution rules, limits, and idempotency.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Planning")]
[Collection("StaticState")]
public class PlanningEngineTests : IntegrationTestBase
{
    /// <summary>
    /// Verifies that DistributeTasks schedules tasks in priority and relevance order.
    /// </summary>
    [Fact]
    public void DistributeTasks_SchedulesTasksInOrderOfRelevance()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var urgentPriority = priorities[0]; // Order 1
        var lowPriority = priorities[3]; // Order 4

        var taskLowPriorityShort = new TaskItemBuilder().WithId(101).WithPriorityId(lowPriority.Id)
            .WithEstimatedMinutes(5).WithInterest(5).WithStatus(TaskStatus.Planned)
            .WithDateSource(DateSource.AutoFlexible).Build();
        var taskUrgentLong = new TaskItemBuilder().WithId(102).WithPriorityId(urgentPriority.Id)
            .WithEstimatedMinutes(30).WithInterest(3).WithStatus(TaskStatus.Planned)
            .WithDateSource(DateSource.AutoFlexible).Build();
        var taskUrgentShortHighInterest = new TaskItemBuilder().WithId(103)
            .WithPriorityId(urgentPriority.Id)
            .WithEstimatedMinutes(5).WithInterest(9).WithStatus(TaskStatus.Planned)
            .WithDateSource(DateSource.AutoFlexible).Build();

        TaskRepo.Add(taskLowPriorityShort);
        TaskRepo.Add(taskUrgentLong);
        TaskRepo.Add(taskUrgentShortHighInterest);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(35).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var saved103 = TaskRepo.GetById(103);
        var saved102 = TaskRepo.GetById(102);
        var saved101 = TaskRepo.GetById(101);

        saved103!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        saved102!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        saved101!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that DistributeTasks splits tasks according to daily time limit.
    /// </summary>
    [Fact]
    public void DistributeTasks_SplitsTasksByDailyTimeLimit()
    {
        // Arrange
        var taskA = new TaskItemBuilder().WithId(101).WithEstimatedMinutes(60)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
        var taskB = new TaskItemBuilder().WithId(102).WithEstimatedMinutes(90)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
        var taskC = new TaskItemBuilder().WithId(103).WithEstimatedMinutes(60)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(taskA);
        TaskRepo.Add(taskB);
        TaskRepo.Add(taskC);

        // Act
        var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();
        PlannerService.DistributeTasks(settings);

        // Assert
        var updatedA = TaskRepo.GetById(taskA.Id);
        var updatedB = TaskRepo.GetById(taskB.Id);
        var updatedC = TaskRepo.GetById(taskC.Id);

        updatedA!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        updatedB!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        updatedC!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that a large task exceeding 70% of daily limit is scheduled on current day exceeding limit.
    /// </summary>
    [Fact]
    public void LargeTask_Exceeding70PercentLimit_ScheduledOnCurrentDayExceedingLimit()
    {
        // Arrange
        var existingTask = new TaskItemBuilder().WithId(201).WithEstimatedMinutes(70)
            .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();
        var largeTask = new TaskItemBuilder().WithId(202).WithEstimatedMinutes(75)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(existingTask);
        TaskRepo.Add(largeTask);

        // Act
        var settings = new UserSettingsBuilder().WithDailyTimeLimit(100).Build();
        PlannerService.DistributeTasks(settings);

        // Assert
        var updatedLarge = TaskRepo.GetById(largeTask.Id);
        updatedLarge!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
    }

    /// <summary>
    /// Verifies that urgent (Priority 0 or 1) tasks are forcibly scheduled today despite full daily limits.
    /// </summary>
    [Fact]
    public void Priority0Or1Task_ForciblyScheduledTodayDespiteFullLimits()
    {
        // Arrange
        var urgentPriority = Context.Priorities.First(p => p.Order == 1);
        var urgentTask = new TaskItemBuilder().WithId(301).WithPriorityId(urgentPriority.Id)
            .WithDateSource(DateSource.AutoFlexible).WithEstimatedMinutes(100)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(urgentTask);

        // Act
        var settings = new UserSettingsBuilder().WithDailyTimeLimit(0).Build();
        PlannerService.DistributeTasks(settings);

        // Assert
        var updatedUrgent = TaskRepo.GetById(urgentTask.Id);
        updatedUrgent!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
    }

    /// <summary>
    /// Verifies that tasks with null fields utilize safe domain validator defaults.
    /// </summary>
    [Fact]
    public void TaskWithNullFields_UsesSafeDefaultsWithoutException()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(400)
            .WithEstimatedMinutes(null)
            .WithComplexity(null)
            .WithInterest(null)
            .Build();

        // Act
        TaskRepo.Add(task);
        var savedTask = TaskRepo.GetById(task.Id);

        // Assert
        savedTask.Should().NotBeNull();
        TaskItemValidator.ClampEstimatedMinutes(savedTask!.EstimatedMinutes).Should().Be(15);
        savedTask.TotalComplexity.Should().Be(0);
        (savedTask.Interest ?? 5).Should().Be(5);
    }

    /// <summary>
    /// Verifies that subtasks are excluded from daily task count limits and subtask duration is aggregated.
    /// </summary>
    [Fact]
    public void DistributeTasks_ExcludesSubtasksFromDailyTaskLimits()
    {
        // Arrange
        var urgentPriority = Context.Priorities.First(p => p.Order == 1);
        var (parentTask, subtasks) = TaskItemBuilder.CreateParentWithSubtasks(2, 10);
        parentTask.PriorityId = urgentPriority.Id;
        parentTask.EstimatedMinutes = 30;
        parentTask.DateSource = DateSource.AutoFlexible;
        parentTask.Status = TaskStatus.Planned;

        foreach (var sub in subtasks)
        {
            sub.EstimatedMinutes = 60;
            sub.DateSource = DateSource.AutoFlexible;
            sub.Status = TaskStatus.Planned;
        }

        TaskRepo.Add(parentTask);

        var settings = new UserSettingsBuilder().WithDailyTaskLimit(1).WithDailyTimeLimit(480).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedParent = TaskRepo.GetById(10);
        savedParent.Should().NotBeNull();
        savedParent!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        savedParent.TotalEstimatedMinutes.Should().Be(150);
    }

    /// <summary>
    /// Verifies that a zero daily time limit executes safely without infinite loops.
    /// </summary>
    [Fact]
    public void ZeroDailyLimit_DoesNotCauseInfiniteLoop()
    {
        // Arrange
        var task = new TaskItemBuilder().WithId(401).WithEstimatedMinutes(60)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
        TaskRepo.Add(task);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(0).Build();

        // Act
        var act = () => PlannerService.DistributeTasks(settings);

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

        TaskRepo.Add(completedToday);
        TaskRepo.Add(newPlannedTask);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedNewTask = TaskRepo.GetById(702);
        savedNewTask!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that daily task count limit stops distribution when limit is reached.
    /// </summary>
    [Fact]
    public void DailyTaskLimit_StopsDistributionWhenLimitReached()
    {
        // Arrange
        var task1 = new TaskItemBuilder().WithId(801).WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned).Build();
        var task2 = new TaskItemBuilder().WithId(802).WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(task1);
        TaskRepo.Add(task2);

        var settings = new UserSettingsBuilder().WithDailyTaskLimit(1).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        TaskRepo.GetById(801)!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        TaskRepo.GetById(802)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that daily task count limit excludes subtasks from counting against limit.
    /// </summary>
    [Fact]
    public void DailyTaskLimit_ExcludesSubtasksFromCount()
    {
        // Arrange
        var (parentTask, _) = TaskItemBuilder.CreateParentWithSubtasks(2, 900);
        parentTask.DateSource = DateSource.AutoFlexible;
        parentTask.Status = TaskStatus.Planned;

        var anotherTask = new TaskItemBuilder().WithId(903).WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(parentTask);
        TaskRepo.Add(anotherTask);

        var settings = new UserSettingsBuilder().WithDailyTaskLimit(1).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        TaskRepo.GetById(900)!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        TaskRepo.GetById(903)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that daily complexity limit stops distribution when limit is reached.
    /// </summary>
    [Fact]
    public void DailyComplexityLimit_StopsDistributionWhenLimitReached()
    {
        // Arrange
        var task1 = new TaskItemBuilder().WithId(1001).WithComplexity(60)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned).Build();
        var task2 = new TaskItemBuilder().WithId(1002).WithComplexity(50)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(task1);
        TaskRepo.Add(task2);

        var settings = new UserSettingsBuilder().WithDailyComplexityLimit(100).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        TaskRepo.GetById(1001)!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        TaskRepo.GetById(1002)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that large task rule applies to complexity limit if task is large by complexity and limit is not yet exhausted.
    /// </summary>
    [Fact]
    public void LargeTaskRule_AppliesToComplexityLimit_WhenTaskIsLargeByComplexity()
    {
        // Arrange
        var existingTask = new TaskItemBuilder().WithId(1101).WithComplexity(50)
            .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var largeComplexityTask = new TaskItemBuilder().WithId(1102).WithComplexity(75)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(existingTask);
        TaskRepo.Add(largeComplexityTask);

        var settings = new UserSettingsBuilder().WithDailyComplexityLimit(100).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        TaskRepo.GetById(1102)!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
    }

    /// <summary>
    /// Verifies that overdue tasks are processed before normal tasks during planning.
    /// </summary>
    [Fact]
    public void OverdueTasks_ProcessedBeforeNormalTasks_CanExceedLimits()
    {
        // Arrange
        var overdueDate = TodoDay.Today.Yesterday.ToDateTime();

        var overdueTask = new TaskItemBuilder()
            .WithId(1201)
            .WithEstimatedMinutes(200)
            .WithScheduledDate(overdueDate, DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();

        var normalTask = new TaskItemBuilder()
            .WithId(1202)
            .WithEstimatedMinutes(30)
            .WithDateSource(DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(overdueTask);
        TaskRepo.Add(normalTask);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedOverdue = TaskRepo.GetById(1201);
        savedOverdue!.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
        savedOverdue.DateSource.Should().Be(DateSource.AutoFlexible);

        TaskRepo.GetById(1202)!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that blocked tasks with fixed dates force AutoFixed dates for blockers when chain exceeds capacity.
    /// </summary>
    [Fact]
    public void BlockedTaskWithFixedDate_WhenChainExceedsCapacity_ForcesAutoFixedDatesForBlockers()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var targetDate = today.AddDays(1);

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
        Context.Tasks.AddRange(blockerA, blockedB);

        Context.TaskRelations.Add(new TaskRelation { Id = 6001, SourceTaskId = blockerA.Id, TargetTaskId = blockedB.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(200).Build();
        PlannerService.DistributeTasks(settings);

        var updatedA = TaskRepo.GetById(blockerA.Id);

        // Assert
        updatedA.Should().NotBeNull();
        updatedA!.DateSource.Should().Be(DateSource.AutoFixed);
    }

    /// <summary>
    /// Verifies that inactive tasks are strictly ignored by planner.
    /// </summary>
    [Fact]
    public void InactiveTasks_AreStrictlyIgnoredByPlanner_AndNeverMoved()
    {
        // Arrange
        var yesterday = TodoDay.Today.Yesterday.ToDateTime();
        var today = TodoDay.Today.ToDateTime();

        var completedTask = new TaskItemBuilder()
            .WithId(2001)
            .WithTitle("Старая завершенная")
            .WithEstimatedMinutes(100)
            .WithScheduledDate(yesterday, DateSource.Manual)
            .WithStatus(TaskStatus.Completed)
            .Build();

        var irrelevantTask = new TaskItemBuilder()
            .WithId(2002)
            .WithTitle("Отмененная сегодня")
            .WithEstimatedMinutes(100)
            .WithScheduledDate(today, DateSource.AutoFlexible)
            .WithStatus(TaskStatus.Irrelevant)
            .Build();

        TaskRepo.Add(completedTask);
        TaskRepo.Add(irrelevantTask);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(50).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedCompleted = TaskRepo.GetById(2001);
        var savedIrrelevant = TaskRepo.GetById(2002);

        savedCompleted!.ScheduledDate.Should().Be(yesterday);
        savedCompleted.DateSource.Should().Be(DateSource.Manual);

        savedIrrelevant!.ScheduledDate.Should().Be(today);
        savedIrrelevant.DateSource.Should().Be(DateSource.AutoFlexible);
    }

    /// <summary>
    /// Verifies that future manual tasks are not pulled to today even if limits allow.
    /// </summary>
    [Fact]
    public void FutureManualTasks_AreNotPulledToToday_EvenIfLimitsAllow()
    {
        // Arrange
        var tomorrow = TodoDay.Today.Tomorrow.ToDateTime();

        var futureManualTask = new TaskItemBuilder()
            .WithId(3001)
            .WithScheduledDate(tomorrow, DateSource.Manual)
            .WithEstimatedMinutes(30)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(futureManualTask);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(480).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedTask = TaskRepo.GetById(3001);
        savedTask!.ScheduledDate.Should().Be(tomorrow);
        savedTask.DateSource.Should().Be(DateSource.Manual);
    }

    /// <summary>
    /// Verifies that recurring tasks ignore daily limits and are never moved by planner.
    /// </summary>
    [Fact]
    public void RecurringTasks_IgnoreDailyLimits_AndAreNeverMovedByPlanner()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();

        var blockingTask = new TaskItemBuilder()
            .WithId(3002).WithDateSource(DateSource.AutoFlexible).WithEstimatedMinutes(180)
            .WithStatus(TaskStatus.Planned).Build();

        var recurringTask = new TaskItemBuilder()
            .WithId(3003).WithRecurrence(RecurrenceType.Daily).WithScheduledDate(today, DateSource.AutoFixed)
            .WithEstimatedMinutes(60).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(blockingTask);
        TaskRepo.Add(recurringTask);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedRecurring = TaskRepo.GetById(3003);
        savedRecurring!.ScheduledDate.Should().Be(today);
    }

    /// <summary>
    /// Verifies that priority escalation does not trigger before escalation date.
    /// </summary>
    [Fact]
    public void PriorityEscalation_DoesNotTrigger_BeforeEscalationDate()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(p => p.Order).ToList();
        var targetPriority = priorities[0];
        var currentPriority = priorities[3];

        var tomorrow = TodoDay.Today.Tomorrow.ToDateTime();

        var task = new TaskItemBuilder()
            .WithId(4001).WithPriorityId(currentPriority.Id).WithStatus(TaskStatus.Planned).Build();

        task.PriorityEscalations.Add(new PriorityEscalation
            { TargetPriorityId = targetPriority.Id, EscalationDate = tomorrow });
        TaskRepo.Add(task);
        Context.SaveChanges();

        // Act
        PlannerService.ActualizePriorities();
        TaskRepo.SaveChanges();

        // Assert
        var savedTask = TaskRepo.GetById(4001);
        savedTask!.PriorityId.Should().Be(currentPriority.Id);
    }

    /// <summary>
    /// Verifies that standard task completion does not trigger recurrence engine.
    /// </summary>
    [Fact]
    public void StandardTaskCompletion_DoesNotTrigger_RecurrenceEngine()
    {
        // Arrange
        var standardTask = new TaskItemBuilder()
            .WithId(5001)
            .WithRecurrence(RecurrenceType.None)
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(standardTask);

        // Act
        TaskRepo.CompleteTask(standardTask.Id);

        // Assert
        var savedTask = TaskRepo.GetById(5001);
        var allTasks = TaskRepo.GetAll();

        savedTask!.Status.Should().Be(TaskStatus.Completed);
        var copies = allTasks.Where(t => t.RecurrenceSourceId == standardTask.Id).ToList();
        copies.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that re-running planning algorithm produces identical dates.
    /// </summary>
    [Fact]
    public void ReRunningPlanningAlgorithm_IsIdempotentAndProducesIdenticalDates()
    {
        // Arrange
        var task1 = new TaskItemBuilder().WithId(501).WithEstimatedMinutes(60)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
        var task2 = new TaskItemBuilder().WithId(502).WithEstimatedMinutes(90)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
        TaskRepo.Add(task1);
        TaskRepo.Add(task2);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(180).Build();

        // Act 1
        PlannerService.DistributeTasks(settings);
        var date1AfterFirst = TaskRepo.GetById(501)!.ScheduledDate;
        var date2AfterFirst = TaskRepo.GetById(502)!.ScheduledDate;

        // Act 2
        PlannerService.DistributeTasks(settings);
        var date1AfterSecond = TaskRepo.GetById(501)!.ScheduledDate;
        var date2AfterSecond = TaskRepo.GetById(502)!.ScheduledDate;

        // Assert
        date1AfterSecond.Should().Be(date1AfterFirst);
        date2AfterSecond.Should().Be(date2AfterFirst);
    }

    /// <summary>
    /// Verifies that inactive tasks completed or marked irrelevant today consume daily limits for auto-flexible tasks.
    /// </summary>
    [Fact]
    public void InactiveTasks_CompletedOrIrrelevantToday_ConsumeDailyLimitsForAutoFlexible()
    {
        // Arrange
        var completedToday = new TaskItemBuilder()
            .WithId(101).WithEstimatedMinutes(60)
            .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual)
            .WithCompletedDate(TodoDay.Today.ToDateTime()).WithStatus(TaskStatus.Completed).Build();

        var irrelevantToday = new TaskItemBuilder()
            .WithId(102).WithEstimatedMinutes(60)
            .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual)
            .WithCompletedDate(TodoDay.Today.ToDateTime()).WithStatus(TaskStatus.Irrelevant).Build();

        var newFlexibleTask = new TaskItemBuilder()
            .WithId(103).WithEstimatedMinutes(60)
            .WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(completedToday);
        TaskRepo.Add(irrelevantToday);
        TaskRepo.Add(newFlexibleTask);

        var settings = new UserSettingsBuilder().WithDailyTimeLimit(150).Build();

        // Act
        PlannerService.DistributeTasks(settings);

        // Assert
        var savedNewTask = TaskRepo.GetById(103);
        savedNewTask!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }

    /// <summary>
    /// Verifies that completing a severely overdue recurring task skips to next actual future date.
    /// </summary>
    [Fact]
    public void SeverelyOverdueRecurringTask_WhenCompleted_SkipsToNextActualFutureDate()
    {
        // Arrange
        var tenDaysAgo = TodoDay.Today.ToDateTime().AddDays(-10);
        var overdueTask = new TaskItemBuilder()
            .WithId(301).WithRecurrence(RecurrenceType.Daily)
            .WithScheduledDate(tenDaysAgo, DateSource.AutoFixed)
            .WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(overdueTask);

        // Act
        TaskRepo.CompleteTask(overdueTask.Id);

        // Assert
        var newCopy = TaskRepo.GetAll().FirstOrDefault(t => t.RecurrenceSourceId == overdueTask.Id);
        newCopy.Should().NotBeNull();
        newCopy!.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
    }
}