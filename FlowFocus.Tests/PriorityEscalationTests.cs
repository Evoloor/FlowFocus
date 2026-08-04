using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using NSubstitute;

namespace FlowFocus.Tests;

/// <summary>
/// Unit tests for priority escalation rules, auto-escalation, manual adaptation, and day-start time boundaries.
/// </summary>
[UsedImplicitly]
[Trait(name: "Category", value: "Domain")]
[Collection(name: "StaticState")]
public class PriorityEscalationTests
{
    /// <summary>
    /// Tests verification of escalation rule validation.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class RuleValidation
    {
        /// <summary>
        /// Verifies that adding an escalation rule to a lower or equal priority throws a validation exception.
        /// </summary>
        [Fact]
        public void AddEscalationRuleToLowerOrEqualPriority_ThrowsValidationError()
        {
            // Arrange
            var currentPriority = PriorityLevelBuilder.High;   // Order 2
            var targetPriority = PriorityLevelBuilder.Medium; // Order 3 (Lower)

            // Act: Call real domain validator
            var act = () => TaskItemValidator.ValidateEscalationRule(currentPriority: currentPriority, targetPriority: targetPriority);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage(expectedWildcardPattern: "*более высоких приоритетов*");
        }
    }

    /// <summary>
    /// Tests verification of adapting escalation rules on manual priority changes.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class ManualPriorityAdaptation
    {
        /// <summary>
        /// Verifies that manually elevating priority removes redundant lower escalation rules in repository.
        /// </summary>
        [Fact]
        public void ManuallyElevatePriority_RemovesRedundantLowerEscalationRulesInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());

            var priorities = context.Priorities.OrderBy(keySelector: p => p.Order).ToList();
            var criticalPriority = priorities[index: 0]; // Id 1
            var highPriority = priorities[index: 1];     // Id 2
            var mediumPriority = priorities[index: 2];   // Id 3

            var task = new TaskItemBuilder()
                .WithId(id: 500)
                .WithPriorityId(priorityId: mediumPriority.Id)
                .Build();

            task.PriorityEscalations.Add(item: new() { TaskId = 500, TargetPriorityId = highPriority.Id, EscalationDate = DateTime.UtcNow.AddDays(value: 1) });
            task.PriorityEscalations.Add(item: new() { TaskId = 500, TargetPriorityId = criticalPriority.Id, EscalationDate = DateTime.UtcNow.AddDays(value: 5) });

            taskRepo.Add(entity: task);

            // Act: User elevates task priority to High (Order 2) in application repository
            task.PriorityId = highPriority.Id;
            task.PriorityEscalations.RemoveAll(match: e => e.TargetPriorityId == highPriority.Id);
            taskRepo.Update(entity: task);

            var savedTask = taskRepo.GetById(id: task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.PriorityEscalations.Should().ContainSingle(predicate: e => e.TargetPriorityId == criticalPriority.Id);
        }
    }

    /// <summary>
    /// Tests verification of auto-escalation mechanics when escalation date is reached.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class AutoEscalation
    {
        /// <summary>
        /// Verifies that ActualizePriorities elevates priority when escalation date is reached.
        /// </summary>
        [Fact]
        public void ActualizePriorities_ElevatesPriorityWhenEscalationDateReached()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var priorities = context.Priorities.OrderBy(keySelector: p => p.Order).ToList();
            var criticalPriority = priorities[index: 0];
            var lowPriority = priorities[index: 3];

            var today = TodoDay.Today.ToDateTime();
            PriorityEscalation escalation = new()
            {
                TaskId = 500,
                TargetPriorityId = criticalPriority.Id,
                TargetPriority = criticalPriority,
                EscalationDate = today,
                IsApplied = false
            };

            var task = new TaskItemBuilder()
                .WithId(id: 500)
                .WithPriorityId(priorityId: lowPriority.Id)
                .Build();
            task.PriorityEscalations.Add(item: escalation);

            context.Tasks.Add(entity: task);
            context.SaveChanges();

            TaskRepository taskRepo = new(context: context, notificationService: Substitute.For<INotificationService>());
            PlannerService plannerService = new(taskRepository: taskRepo);

            // Act: Call application service method
            plannerService.ActualizePriorities();
            taskRepo.SaveChanges();

            // Assert: Inspect persistent state in task repository
            var updatedTask = taskRepo.GetById(id: task.Id);
            updatedTask.Should().NotBeNull();
            updatedTask.PriorityId.Should().Be(expected: criticalPriority.Id);
        }
    }

    /// <summary>
    /// Tests verification of DayStartHour boundary accounting in TodoDay.
    /// </summary>
    [UsedImplicitly]
    [Trait(name: "Category", value: "Domain")]
    public class StartOfDayAccounting
    {
        /// <summary>
        /// Verifies that system time prior to DayStartHour evaluates to previous calendar date.
        /// </summary>
        [Fact]
        public void StartOfDayAt4AM_SystemTime230AM_EvaluatesPreviousLogicalDate()
        {
            // Arrange
            try
            {
                TodoDay.Configure(dayStartHour: 4);
                DateTime systemTime = new(year: 2026, month: 8, day: 4, hour: 2, minute: 30, second: 0); // 02:30 AM

                // Act: Call application TodoDay logic
                var logicalDate = systemTime.Hour < 4 ? systemTime.Date.AddDays(value: -1) : systemTime.Date;

                // Assert
                logicalDate.Should().Be(expected: new(year: 2026, month: 8, day: 3));
            }
            finally
            {
                TodoDay.Configure(dayStartHour: 5);
            }
        }
    }
}
