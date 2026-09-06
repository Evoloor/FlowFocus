using FlowFocus.Core.Models;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

/// <summary>
/// Integration unit tests for TaskRepository CRUD lifecycle, database hygiene, and orphan tag cleanup.
/// </summary>
[UsedImplicitly]
[Trait("Category", "Database")]
[Collection("StaticState")]
public class TaskRepositoryTests : IntegrationTestBase
{
    [Fact]
    public void HardDeleteTask_WithExclusiveTag_RemovesOrphanedTag()
    {
        // Arrange: Жизненный цикл тегов - удаление "сирот"
        Tag tag = new() { Id = 10, Name = "Одноразовый", UsageCount = 1 };
        Context.Tags.Add(tag);

        var task = new TaskItemBuilder()
            .WithId(999)
            .WithTitle("Задача на удаление")
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);
        Context.TaskTags.Add(new() { TaskId = task.Id, TagId = tag.Id });
        Context.SaveChanges();

        // Act: Безвозвратное удаление задачи из БД
        TaskRepo.Delete(task.Id);

        // Assert: Задача, связь и сам осиротевший тег должны исчезнуть
        var deletedTask = TaskRepo.GetById(task.Id);
        deletedTask.Should().BeNull("Задача должна быть удалена");

        var orphanedTag = Context.Tags.FirstOrDefault(t => t.Id == tag.Id);
        orphanedTag.Should().BeNull("Тег, который больше не используется ни в одной задаче, должен удаляться, чтобы не создавать мусорку в БД");
    }

    [Fact]
    public void HardDeleteTask_WithSharedTag_KeepsTagForOtherTasks()
    {
        // Arrange: Проверка негативного сценария - не удаляем тег, если он нужен другим
        Tag sharedTag = new() { Id = 11, Name = "Общий", UsageCount = 2 };
        Context.Tags.Add(sharedTag);

        var taskToDelete = new TaskItemBuilder().WithId(998).Build();
        var taskToKeep = new TaskItemBuilder().WithId(997).Build();

        TaskRepo.Add(taskToDelete);
        TaskRepo.Add(taskToKeep);

        Context.TaskTags.Add(new() { TaskId = taskToDelete.Id, TagId = sharedTag.Id });
        Context.TaskTags.Add(new() { TaskId = taskToKeep.Id, TagId = sharedTag.Id });
        Context.SaveChanges();

        // Act
        TaskRepo.Delete(taskToDelete.Id);

        // Assert
        var remainingTag = Context.Tags.FirstOrDefault(t => t.Id == sharedTag.Id);
        remainingTag.Should().NotBeNull("Тег должен остаться, так как используется в другой задаче");
    }

    [Fact]
    public void UpdateTaskStatus_WhenStatusChangedToNonCompleted_ClearsCompletedDate()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(805)
            .WithTitle("Тестовая задача")
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(task);
        TaskRepo.CompleteTask(task.Id);

        var completed = TaskRepo.GetById(task.Id);
        completed!.Status.Should().Be(TaskStatus.Completed);
        completed.CompletedDate.Should().NotBeNull();

        // Act: переводим в Planned через UpdateTaskStatus
        TaskRepo.UpdateTaskStatus(task.Id, TaskStatus.Planned);

        // Assert: CompletedDate сбросился в null
        var updated = TaskRepo.GetById(task.Id);
        updated!.Status.Should().Be(TaskStatus.Planned);
        updated.CompletedDate.Should().BeNull("При переводе в Planned через репозиторий CompletedDate сбрасывается");
    }

    [Fact]
    public void MarkIrrelevant_EnsuresCompletedDateIsNull()
    {
        // Arrange: завершённая задача с заполненной CompletedDate
        var task = new TaskItemBuilder()
            .WithId(801)
            .WithTitle("Задача для отмены")
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(task);
        TaskRepo.CompleteTask(task.Id);

        var completed = TaskRepo.GetById(task.Id);
        completed!.Status.Should().Be(TaskStatus.Completed);
        completed.CompletedDate.Should().NotBeNull();

        // Act: помечаем неактуальной
        TaskRepo.MarkIrrelevant(task.Id);

        // Assert
        var irrelevant = TaskRepo.GetById(task.Id);
        irrelevant.Should().NotBeNull();
        irrelevant!.Status.Should().Be(TaskStatus.Irrelevant);
        irrelevant.CompletedDate.Should().BeNull("У неактуальной задачи CompletedDate обязан быть null");
    }

    [Fact]
    public void RestoreFromIrrelevant_EnsuresCompletedDateIsNull()
    {
        // Arrange: неактуальная задача
        var task = new TaskItemBuilder()
            .WithId(802)
            .WithTitle("Неактуальная задача")
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(task);
        TaskRepo.MarkIrrelevant(task.Id);

        // Act: восстанавливаем из неактуальных
        TaskRepo.RestoreFromIrrelevant(task.Id);

        // Assert
        var restored = TaskRepo.GetById(task.Id);
        restored.Should().NotBeNull();
        restored!.Status.Should().Be(TaskStatus.Planned);
        restored.CompletedDate.Should().BeNull("После восстановления CompletedDate обязан быть null");
    }

    [Fact]
    public void ReopenTask_ClearsCompletedDate()
    {
        // Arrange: выполняем задачу
        var task = new TaskItemBuilder()
            .WithId(803)
            .WithTitle("Выполненная задача")
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(task);
        TaskRepo.CompleteTask(task.Id);

        var completed = TaskRepo.GetById(task.Id);
        completed!.Status.Should().Be(TaskStatus.Completed);
        completed.CompletedDate.Should().NotBeNull();

        // Act: отменяем выполнение (ReopenTask)
        TaskRepo.ReopenTask(task.Id);

        // Assert
        var reopened = TaskRepo.GetById(task.Id);
        reopened.Should().NotBeNull();
        reopened!.Status.Should().Be(TaskStatus.Planned);
        reopened.CompletedDate.Should().BeNull("При отмене выполнения CompletedDate обязан сбрасываться в null");
    }

    [Fact]
    public void StorageContext_SaveChanges_EnforcesCompletedDateNullForNonCompletedTasks()
    {
        // Arrange & Act 1: сохраняем новую задачу со статусом Planned и явно заполненной CompletedDate
        var task = new TaskItem
        {
            Id = 804,
            Title = "Низкоуровневая задача",
            Status = TaskStatus.Planned,
            CompletedDate = DateTime.UtcNow
        };
        Context.Tasks.Add(task);
        Context.SaveChanges();

        // Assert 1: ChangeTracker в SaveChanges обязан сбросить дату в null
        var savedTask = Context.Tasks.AsNoTracking().FirstOrDefault(t => t.Id == 804);
        savedTask.Should().NotBeNull();
        savedTask!.Status.Should().Be(TaskStatus.Planned);
        savedTask.CompletedDate.Should().BeNull("StorageContext.SaveChanges обязан обнулять CompletedDate для не-Completed задач при добавлении");

        // Act 2: модифицируем сущность, пытаясь присвоить CompletedDate при Planned
        var trackedTask = Context.Tasks.Find(804);
        trackedTask!.CompletedDate = DateTime.UtcNow;
        Context.SaveChanges();

        // Assert 2: при обновлении дата также должна сброситься в null
        var updatedTask = Context.Tasks.AsNoTracking().FirstOrDefault(t => t.Id == 804);
        updatedTask.Should().NotBeNull();
        updatedTask!.CompletedDate.Should().BeNull("StorageContext.SaveChanges обязан обнулять CompletedDate для не-Completed задач при обновлении");
    }
}
