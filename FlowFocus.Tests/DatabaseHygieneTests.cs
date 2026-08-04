using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using JetBrains.Annotations;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[UsedImplicitly]
[Collection(name: "StaticState")]
public class DatabaseHygieneTests
{
    [Fact]
    public void HardDeleteTask_WithExclusiveTag_RemovesOrphanedTag()
    {
        // Arrange: Жизненный цикл тегов - удаление "сирот"[cite: 3]
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());
            
        // Предполагаем, что у нас есть механизм работы с тегами
        var tag = new Tag { Id = 10, Name = "Одноразовый", UsageCount = 1 };
        context.Tags.Add(tag);

        var task = new TaskItemBuilder()
            .WithId(999)
            .WithTitle("Задача на удаление")
            .WithStatus(TaskStatus.Planned)
            .Build();
            
        taskRepo.Add(task);
        context.TaskTags.Add(new TaskTag { TaskId = task.Id, TagId = tag.Id });
        context.SaveChanges();

        // Act: Безвозвратное удаление задачи из БД
        taskRepo.Delete(task.Id); 

        // Assert: Задача, связь и сам осиротевший тег должны исчезнуть
        var deletedTask = taskRepo.GetById(task.Id);
        deletedTask.Should().BeNull("Задача должна быть удалена");

        var orphanedTag = context.Tags.FirstOrDefault(t => t.Id == tag.Id);
        orphanedTag.Should().BeNull("Тег, который больше не используется ни в одной задаче, должен удаляться, чтобы не создавать мусорку в БД");
    }
        
    [Fact]
    public void HardDeleteTask_WithSharedTag_KeepsTagForOtherTasks()
    {
        // Arrange: Проверка негативного сценария - не удаляем тег, если он нужен другим
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

        var sharedTag = new Tag { Id = 11, Name = "Общий", UsageCount = 2 };
        context.Tags.Add(sharedTag);

        var taskToDelete = new TaskItemBuilder().WithId(998).Build();
        var taskToKeep = new TaskItemBuilder().WithId(997).Build();
            
        taskRepo.Add(taskToDelete);
        taskRepo.Add(taskToKeep);
            
        context.TaskTags.Add(new TaskTag { TaskId = taskToDelete.Id, TagId = sharedTag.Id });
        context.TaskTags.Add(new TaskTag { TaskId = taskToKeep.Id, TagId = sharedTag.Id });
        context.SaveChanges();

        // Act
        taskRepo.Delete(taskToDelete.Id);

        // Assert
        var remainingTag = context.Tags.FirstOrDefault(t => t.Id == sharedTag.Id);
        remainingTag.Should().NotBeNull("Тег должен остаться, так как используется в другой задаче");
    }
}