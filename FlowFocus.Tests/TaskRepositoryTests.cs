using FlowFocus.Core.Models;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using JetBrains.Annotations;
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
        var tag = new Tag { Id = 10, Name = "Одноразовый", UsageCount = 1 };
        Context.Tags.Add(tag);

        var task = new TaskItemBuilder()
            .WithId(999)
            .WithTitle("Задача на удаление")
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(task);
        Context.TaskTags.Add(new TaskTag { TaskId = task.Id, TagId = tag.Id });
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
        var sharedTag = new Tag { Id = 11, Name = "Общий", UsageCount = 2 };
        Context.Tags.Add(sharedTag);

        var taskToDelete = new TaskItemBuilder().WithId(998).Build();
        var taskToKeep = new TaskItemBuilder().WithId(997).Build();

        TaskRepo.Add(taskToDelete);
        TaskRepo.Add(taskToKeep);

        Context.TaskTags.Add(new TaskTag { TaskId = taskToDelete.Id, TagId = sharedTag.Id });
        Context.TaskTags.Add(new TaskTag { TaskId = taskToKeep.Id, TagId = sharedTag.Id });
        Context.SaveChanges();

        // Act
        TaskRepo.Delete(taskToDelete.Id);

        // Assert
        var remainingTag = Context.Tags.FirstOrDefault(t => t.Id == sharedTag.Id);
        remainingTag.Should().NotBeNull("Тег должен остаться, так как используется в другой задаче");
    }
}
