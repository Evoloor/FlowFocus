using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Trait("Category", "Planning")]
public class PlanningEngineTests
{
    private static StorageContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new StorageContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public class SortingRules
    {
        [Fact]
        public void DistributeTasks_SchedulesTasksInOrderOfRelevance()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
            var urgentPriority = priorities[0]; // Order 1
            var lowPriority = priorities[3];    // Order 4

            var taskLowPriorityShort = new TaskItemBuilder().WithId(101).WithPriorityId(lowPriority.Id).WithEstimatedMinutes(5).WithInterest(5).WithStatus(TaskStatus.Planned).WithDateSource(DateSource.AutoFlexible).Build();
            var taskUrgentLong = new TaskItemBuilder().WithId(102).WithPriorityId(urgentPriority.Id).WithEstimatedMinutes(30).WithInterest(3).WithStatus(TaskStatus.Planned).WithDateSource(DateSource.AutoFlexible).Build();
            var taskUrgentShortHighInterest = new TaskItemBuilder().WithId(103).WithPriorityId(urgentPriority.Id).WithEstimatedMinutes(5).WithInterest(9).WithStatus(TaskStatus.Planned).WithDateSource(DateSource.AutoFlexible).Build();

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
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var taskA = new TaskItemBuilder().WithId(101).WithEstimatedMinutes(60).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            var taskB = new TaskItemBuilder().WithId(102).WithEstimatedMinutes(90).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            var taskC = new TaskItemBuilder().WithId(103).WithEstimatedMinutes(60).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

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
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var existingTask = new TaskItemBuilder().WithId(201).WithEstimatedMinutes(70).WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual).WithStatus(TaskStatus.Planned).Build();
            var largeTask = new TaskItemBuilder().WithId(202).WithEstimatedMinutes(75).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();

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
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var urgentPriority = context.Priorities.First(p => p.Order == 1);
            var urgentTask = new TaskItemBuilder().WithId(301).WithPriorityId(urgentPriority.Id).WithDateSource(DateSource.AutoFlexible).WithEstimatedMinutes(100).WithStatus(TaskStatus.Planned).Build();

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
            using var context = CreateInMemoryContext();
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
            savedTask.TotalEstimatedMinutes.Should().Be(0);
            savedTask.TotalComplexity.Should().Be(0);
            (savedTask.Interest ?? 5).Should().Be(5);
        }

        [Fact]
        public void ZeroDailyLimit_DoesNotCauseInfiniteLoop()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var task = new TaskItemBuilder().WithId(401).WithEstimatedMinutes(60).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            taskRepo.Add(task);

            var settings = new UserSettingsBuilder().WithDailyTimeLimit(0).Build();

            // Act
            var act = () => plannerService.DistributeTasks(settings);

            // Assert
            act.Should().NotThrow();
        }
    }

    public class BlockerDateCalculation
    {
        [Fact]
        public void BlockedTaskWithFixedDate_WhenChainExceedsCapacity_ForcesAutoFixedDatesForBlockers()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var today = TodoDay.Today.ToDateTime();
            var targetDate = today.AddDays(3);

            var blockerA = new TaskItem { Id = 601, Title = "Blocker A", DateSource = DateSource.AutoFlexible, Status = TaskStatus.Planned, EstimatedMinutes = 300 };
            var blockedB = new TaskItem { Id = 602, Title = "Blocked B", ScheduledDate = targetDate, DateSource = DateSource.Manual, Status = TaskStatus.Planned, EstimatedMinutes = 300 };
            context.Tasks.AddRange(blockerA, blockedB);

            context.TaskRelations.Add(new TaskRelation { Id = 6001, SourceTaskId = blockerA.Id, TargetTaskId = blockedB.Id, Type = RelationType.Blocks });
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
            using var context = CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            var task1 = new TaskItemBuilder().WithId(501).WithEstimatedMinutes(60).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
            var task2 = new TaskItemBuilder().WithId(502).WithEstimatedMinutes(90).WithDateSource(DateSource.AutoFlexible).WithStatus(TaskStatus.Planned).Build();
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
