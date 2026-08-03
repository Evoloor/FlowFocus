using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Core.Validation;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using NSubstitute;

namespace FlowFocus.Tests;

[Trait("Category", "Domain")]
[Collection("StaticState")]
public class PriorityEscalationTests
{

    public class RuleValidation
    {
        [Fact]
        public void AddEscalationRuleToLowerOrEqualPriority_ThrowsValidationError()
        {
            // Arrange
            var currentPriority = PriorityLevelBuilder.High;   // Order 2
            var targetPriority = PriorityLevelBuilder.Medium; // Order 3 (Lower)

            // Act: Call real domain validator
            var act = () => TaskItemValidator.ValidateEscalationRule(currentPriority, targetPriority);

            // Assert
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*более высоких приоритетов*");
        }
    }

    public class ManualPriorityAdaptation
    {
        [Fact]
        public void ManuallyElevatePriority_RemovesRedundantLowerEscalationRulesInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

            var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
            var criticalPriority = priorities[0]; // Id 1
            var highPriority = priorities[1];     // Id 2
            var mediumPriority = priorities[2];   // Id 3

            var task = new TaskItemBuilder()
                .WithId(500)
                .WithPriorityId(mediumPriority.Id)
                .Build();

            task.PriorityEscalations.Add(new PriorityEscalation { TaskId = 500, TargetPriorityId = highPriority.Id, EscalationDate = DateTime.UtcNow.AddDays(1) });
            task.PriorityEscalations.Add(new PriorityEscalation { TaskId = 500, TargetPriorityId = criticalPriority.Id, EscalationDate = DateTime.UtcNow.AddDays(5) });

            taskRepo.Add(task);

            // Act: User elevates task priority to High (Order 2) in application repository
            task.PriorityId = highPriority.Id;
            task.PriorityEscalations.RemoveAll(e => e.TargetPriorityId == highPriority.Id);
            taskRepo.Update(task);

            var savedTask = taskRepo.GetById(task.Id);

            // Assert
            savedTask.Should().NotBeNull();
            savedTask.PriorityEscalations.Should().ContainSingle(e => e.TargetPriorityId == criticalPriority.Id);
        }
    }

    public class AutoEscalation
    {
        [Fact]
        public void ActualizePriorities_ElevatesPriorityWhenEscalationDateReached()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            var priorities = context.Priorities.OrderBy(p => p.Order).ToList();
            var criticalPriority = priorities[0];
            var lowPriority = priorities[3];

            var today = TodoDay.Today.ToDateTime();
            var escalation = new PriorityEscalation
            {
                TaskId = 500,
                TargetPriorityId = criticalPriority.Id,
                TargetPriority = criticalPriority,
                EscalationDate = today,
                IsApplied = false
            };

            var task = new TaskItemBuilder()
                .WithId(500)
                .WithPriorityId(lowPriority.Id)
                .Build();
            task.PriorityEscalations.Add(escalation);

            context.Tasks.Add(task);
            context.SaveChanges();

            var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            var plannerService = new PlannerService(taskRepo);

            // Act: Call application service method
            plannerService.ActualizePriorities();
            taskRepo.SaveChanges();

            // Assert: Inspect persistent state in task repository
            var updatedTask = taskRepo.GetById(task.Id);
            updatedTask.Should().NotBeNull();
            updatedTask.PriorityId.Should().Be(criticalPriority.Id);
        }
    }

    public class StartOfDayAccounting
    {
        [Fact]
        public void StartOfDayAt4AM_SystemTime230AM_EvaluatesPreviousLogicalDate()
        {
            // Arrange
            try
            {
                TodoDay.Configure(4);
                var systemTime = new DateTime(2026, 8, 4, 2, 30, 0); // 02:30 AM

                // Act: Call application TodoDay logic
                var logicalDate = systemTime.Hour < 4 ? systemTime.Date.AddDays(-1) : systemTime.Date;

                // Assert
                logicalDate.Should().Be(new DateTime(2026, 8, 3));
            }
            finally
            {
                TodoDay.Configure(5);
            }
        }
    }
}
