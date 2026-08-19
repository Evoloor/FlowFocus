using FluentAssertions;
using FlowFocus.Blazor.EditDialogContents;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Validation;
using FlowFocus.Data.Services;
using FlowFocus.Tests.Builders;
using JetBrains.Annotations;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Юнит и интеграционные тесты для фикса самоблокировок и нормализации связей.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Relations")]
[Collection("StaticState")]
public class RelationFixTests : IntegrationTestBase
{
    [Fact]
    public void CreateNewTask_WithBlockedByRelation_SavesCorrectSourceAndTargetIdsWithoutSelfReference()
    {
        // Arrange: Существующая задача Б (ID = 500)
        var taskB = new TaskItemBuilder().WithId(500).WithTitle("Задача Б").Build();
        TaskRepo.Add(taskB);

        // Новая задача А (ID = 0)
        var taskA = new TaskItemBuilder().WithId(0).WithTitle("Задача А").Build();

        List<RelationDto> dtos =
        [
            new RelationDto
            {
                Type = RelationType.BlockedBy,
                TargetTask = taskB
            }
        ];

        // Act: Синхронизируем связи и добавляем новую задачу А
        var (outgoing, incoming) = RelationModule.SyncRelationsToTask(dtos, taskA, null);
        taskA.Relations = outgoing;
        taskA.InverseRelations = incoming;

        TaskRepo.Add(taskA);

        // Assert
        taskA.Id.Should().BeGreaterThan(0, "Новой задаче должен быть присвоен ID");
        taskA.Id.Should().NotBe(500);

        var savedTaskA = TaskRepo.GetById(taskA.Id);
        savedTaskA.Should().NotBeNull();

        // Не должно быть выходящих самоблокировок у А
        savedTaskA!.Relations.Should().BeEmpty();

        // У задачи А должна быть входящая связь от Б
        var inverseRelations = Context.TaskRelations.Where(r => r.TargetTaskId == taskA.Id).ToList();
        inverseRelations.Should().ContainSingle();

        var relation = inverseRelations.Single();
        relation.SourceTaskId.Should().Be(500, "Источником блокера должна быть задача Б");
        relation.TargetTaskId.Should().Be(taskA.Id, "Целью блокера должна быть задача А");
        relation.SourceTaskId.Should().NotBe(relation.TargetTaskId, "Связь не должна ссылаться сама на себя");
    }

    [Fact]
    public void SelfReferencingRelation_ThrowsValidationException()
    {
        // Arrange
        var task = new TaskItemBuilder().WithId(100).WithTitle("Задача").Build();

        // Act
        var act = () => TaskRelationValidator.ValidateNewRelation(task, task, RelationType.Blocks);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*не может быть связана сама с собой*");
    }

    [Fact]
    public void NormalizeTaskRelations_RemovesSelfReferencingRelations_AndAddsBrokenTag()
    {
        // Arrange: Добавляем задачу и битую самоблокировку
        var brokenTask = new TaskItemBuilder().WithId(999).WithTitle("Сломанная задача").WithStatus(TaskStatus.Blocked).Build();
        TaskRepo.Add(brokenTask);

        Context.TaskRelations.Add(new TaskRelation
        {
            Id = 9999,
            SourceTaskId = 999,
            TargetTaskId = 999,
            Type = RelationType.Blocks
        });
        Context.SaveChanges();

        // Act: Вызываем нормализацию связей
        TaskRepo.NormalizeTaskRelations(saveChanges: true);

        // Assert: Самоблокировка удалена, статус задачи обновился, назначен тег "проверить_блокер"
        var selfRelationsInDb = Context.TaskRelations.Where(r => r.SourceTaskId == 999 && r.TargetTaskId == 999).ToList();
        selfRelationsInDb.Should().BeEmpty("Самоблокировки должны быть удалены при нормализации");

        var updatedTask = TaskRepo.GetById(999);
        updatedTask.Should().NotBeNull();
        updatedTask!.Status.Should().NotBe(TaskStatus.Blocked);

        var hasTag = Context.TaskTags.Any(tt => tt.TaskId == 999 && tt.Tag != null && tt.Tag.Name == RelationNormalizer.BrokenBlockerTagName);
        hasTag.Should().BeTrue("Сломанная задача должна получить тег 'проверить_блокер'");
    }
}
