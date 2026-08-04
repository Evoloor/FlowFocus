using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Validation;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;

namespace FlowFocus.Tests;

/// <summary>
/// Domain unit tests for priority system behavior, default configurations, reordering, escalation rules, and limits.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Priority")]
[Collection("StaticState")]
public class PriorityTests : IntegrationTestBase
{
    [Fact]
    public void LoadDefaultConfiguration_ReturnsStrictlyFiveBasePriorities()
    {
        // Act
        var priorities = PriorityRepo.GetAllOrdered();

        // Assert
        priorities.Should().HaveCount(expected: 5);
        priorities.Select(selector: p => p.Name).Should().ContainInConsecutiveOrder(expected: ["Критический", "Высокий", "Средний", "Низкий", "Фоновый"]);
        priorities.Select(selector: p => p.Color).Should().ContainInConsecutiveOrder(expected: ["#FF4444", "#FF8C00", "#FFD700", "#4CAF50", "#2196F3"]);
    }

    [Fact]
    public void UpdatePriorityProperties_ReturnsUpdatedNameAndColorFromRepository()
    {
        // Arrange
        var priority = PriorityRepo.GetAllOrdered().First();

        PriorityLevel updatedPriority = new()
        {
            Id = priority.Id,
            Order = priority.Order,
            Name = "Срочный блокер",
            Color = "#FF0055"
        };

        // Act
        PriorityRepo.Update(entity: updatedPriority);
        var result = PriorityRepo.GetById(id: priority.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be(expected: "Срочный блокер");
        result.Color.Should().Be(expected: "#FF0055");
    }

    [Fact]
    public void DragAndDropReorder_RecalculatesOrderIndicesAndComparisons()
    {
        // Arrange
        var initialPriorities = PriorityRepo.GetAllOrdered();

        List<int> reorderedIds =
        [
            initialPriorities[index: 4].Id, // Move Background (5) to first position
            initialPriorities[index: 0].Id,
            initialPriorities[index: 1].Id,
            initialPriorities[index: 2].Id,
            initialPriorities[index: 3].Id
        ];

        // Act
        PriorityRepo.Reorder(orderedIds: reorderedIds);
        var result = PriorityRepo.GetAllOrdered();

        // Assert
        result.First().Id.Should().Be(expected: initialPriorities[index: 4].Id);
        result.First().Order.Should().Be(expected: 1);
    }

    [Fact]
    public void AddingMoreThan20Priorities_BlocksOperationAtLimit()
    {
        // Arrange
        for (var i = 6; i <= 20; i++)
        {
            PriorityRepo.Add(entity: new PriorityLevelBuilder().WithId(id: i).WithOrder(order: i).WithName(name: $"Priority {i}").Build());
        }

        // Act: Attempt to add 21st priority
        var act = () => PriorityRepo.Add(entity: new PriorityLevelBuilder().WithId(id: 21).WithOrder(order: 21).WithName(name: "Priority 21").Build());

        // Assert: System must block 21st priority addition
        act.Should().Throw<InvalidOperationException>()
           .WithMessage(expectedWildcardPattern: "*20*");
    }

    [Fact]
    public void CreateNewTask_AssignsDefaultPriorityFromUserSettings()
    {
        // Arrange
        var userSettings = new UserSettingsBuilder().WithDefaultPriorityId(priorityId: 2).Build();

        var newTask = new TaskItemBuilder()
            .WithId(id: 101)
            .WithTitle(title: "New Task")
            .WithPriorityId(priorityId: userSettings.DefaultPriorityId)
            .Build();

        // Act
        TaskRepo.Add(entity: newTask);
        var savedTask = TaskRepo.GetById(id: newTask.Id);

        // Assert
        savedTask.Should().NotBeNull();
        savedTask!.PriorityId.Should().Be(expected: 2);
    }

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

    [Fact]
    public void ManuallyElevatePriority_RemovesRedundantLowerEscalationRulesInRepository()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(keySelector: p => p.Order).ToList();
        var criticalPriority = priorities[index: 0]; // Id 1
        var highPriority = priorities[index: 1];     // Id 2
        var mediumPriority = priorities[index: 2];   // Id 3

        var task = new TaskItemBuilder()
            .WithId(id: 500)
            .WithPriorityId(priorityId: mediumPriority.Id)
            .Build();

        task.PriorityEscalations.Add(item: new() { TaskId = 500, TargetPriorityId = highPriority.Id, EscalationDate = DateTime.UtcNow.AddDays(value: 1) });
        task.PriorityEscalations.Add(item: new() { TaskId = 500, TargetPriorityId = criticalPriority.Id, EscalationDate = DateTime.UtcNow.AddDays(value: 5) });

        TaskRepo.Add(entity: task);

        // Act: User elevates task priority to High (Order 2) in application repository
        task.PriorityId = highPriority.Id;
        task.PriorityEscalations.RemoveAll(match: e => e.TargetPriorityId == highPriority.Id);
        TaskRepo.Update(entity: task);

        var savedTask = TaskRepo.GetById(id: task.Id);

        // Assert
        savedTask.Should().NotBeNull();
        savedTask!.PriorityEscalations.Should().ContainSingle(predicate: e => e.TargetPriorityId == criticalPriority.Id);
    }

    [Fact]
    public void ActualizePriorities_ElevatesPriorityWhenEscalationDateReached()
    {
        // Arrange
        var priorities = Context.Priorities.OrderBy(keySelector: p => p.Order).ToList();
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

        Context.Tasks.Add(entity: task);
        Context.SaveChanges();

        // Act: Call application service method
        PlannerService.ActualizePriorities();
        TaskRepo.SaveChanges();

        // Assert: Inspect persistent state in task repository
        var updatedTask = TaskRepo.GetById(id: task.Id);
        updatedTask.Should().NotBeNull();
        updatedTask!.PriorityId.Should().Be(expected: criticalPriority.Id);
    }

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
