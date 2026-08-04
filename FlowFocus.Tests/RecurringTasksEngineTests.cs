using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Data.Services;
using FlowFocus.Tests.Builders;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

[Trait("Category", "Recurrence")]
[Collection("StaticState")]
public class RecurringTasksEngineTests
{
    public class DailyRecurrence
    {
        [Fact]
        public void CompleteDailyTask_CreatesNewCopyForTomorrowInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context, Substitute.For<INotificationService>());

            var today = TodoDay.Today.ToDateTime();
            var task = new TaskItemBuilder()
                .WithId(100)
                .WithTitle("Daily Task")
                .WithScheduledDate(today, DateSource.AutoFixed)
                .WithRecurrence(RecurrenceType.Daily)
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(task);

            // Act: Call real application service method
            taskRepo.CompleteTask(task.Id);

            var allTasks = taskRepo.GetAll();
            var completedTask = taskRepo.GetById(task.Id);
            var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == task.Id);

            // Assert: Verify persistent repository state
            completedTask!.Status.Should().Be(TaskStatus.Completed);
            newCopy.Should().NotBeNull();
            newCopy.Title.Should().Be("Daily Task");
            newCopy.ScheduledDate.Should().Be(today.AddDays(1));
            newCopy.DateSource.Should().Be(DateSource.AutoFixed);
            newCopy.Status.Should().Be(TaskStatus.Planned);
        }
    }

    public class OverdueCompletion
    {
        [Fact]
        public void CompleteOverdueTask_CalculatesNextDateFromActualCompletionDate()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context, Substitute.For<INotificationService>());

            var overdueDate = TodoDay.Today.Yesterday.AddDays(-2).ToDateTime(); // 3 days ago
            var task = new TaskItemBuilder()
                .WithId(200)
                .WithTitle("Overdue Daily Task")
                .WithScheduledDate(overdueDate, DateSource.AutoFixed)
                .WithRecurrence(RecurrenceType.Daily)
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(task);

            // Act: Call application service method to complete today
            taskRepo.CompleteTask(task.Id);

            var allTasks = taskRepo.GetAll();
            var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == task.Id);

            // Assert: Scheduled date calculated from actual completion day (Today + 1 day)
            newCopy.Should().NotBeNull();
            newCopy.ScheduledDate.Should().Be(TodoDay.Today.Tomorrow.ToDateTime());
        }
    }

    public class MonthlyYearly
    {
        [Fact]
        public void CompleteMonthlyTask_CreatesCopyNextMonth()
        {
            // Arrange
            TaskRecurrenceService recurrenceService = new();
            DateTime aug3 = new(2026, 8, 3);

            var task = new TaskItemBuilder()
                .WithId(300)
                .WithRecurrence(RecurrenceType.Monthly)
                .WithCompletedDate(aug3)
                .Build();

            // Act: Call real recurrence service
            var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

            // Assert
            nextDate.Should().Be(new(2026, 9, 3));
        }

        [Fact]
        public void CompleteJan31MonthlyTask_CalculatesFeb28WithoutInvalidDateError()
        {
            // Arrange
            TaskRecurrenceService recurrenceService = new();
            DateTime jan31 = new(2026, 1, 31);

            var task = new TaskItemBuilder()
                .WithId(301)
                .WithRecurrence(RecurrenceType.Monthly)
                .WithScheduledDate(jan31)
                .WithCompletedDate(jan31)
                .Build();

            // Act: Call real recurrence service
            var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

            // Assert: Handles month end boundary safely
            nextDate.Should().Be(new(2026, 2, 28));
        }

        [Fact]
        public void CompleteYearlyTask_CreatesCopyNextYear()
        {
            // Arrange
            TaskRecurrenceService recurrenceService = new();
            DateTime aug3 = new(2026, 8, 3);

            var task = new TaskItemBuilder()
                .WithId(302)
                .WithRecurrence(RecurrenceType.Yearly)
                .WithScheduledDate(aug3)
                .WithCompletedDate(aug3)
                .Build();

            // Act: Call real recurrence service
            var nextDate = recurrenceService.CalculateNextRecurrenceDate(task);

            // Assert: Date set to same day next year (2027-08-03)
            nextDate.Should().Be(new(2027, 8, 3));
        }
    }

    public class CascadeSubtaskCopy
    {
        [Fact]
        public void CompleteRecurringTask_CascadesSubtasksToNewCopyInRepository()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context, Substitute.For<INotificationService>());

            TaskItem sub1 = new() { Title = "Subtask 1" };
            TaskItem sub2 = new() { Title = "Subtask 2" };

            TaskItem parent = new()
            {
                Title = "Parent Recurring Task",
                IsRecurring = true,
                RecurrenceType = RecurrenceType.Daily,
                ScheduledDate = TodoDay.Today.ToDateTime(),
                Status = TaskStatus.Planned,
                Subtasks = [sub1, sub2]
            };

            taskRepo.Add(parent);
            context.ChangeTracker.Clear();

            // Act: Call application service method
            taskRepo.CompleteTask(parent.Id);

            var allTasks = taskRepo.GetAll();
            var newCopy = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == parent.Id);

            // Assert: Subtasks copied and attached to new parent copy in DB
            newCopy.Should().NotBeNull();
            newCopy.Subtasks.Should().HaveCount(2);
            newCopy.Subtasks.Select(s => s.Title).Should().ContainInConsecutiveOrder("Subtask 1", "Subtask 2");
        }
    }

    public class RapidClicksIdempotency
    {
        [Fact]
        public void RapidClicksOnCompletion_GeneratesOnlySingleCopy()
        {
            // Arrange
            using var context = TestDbContextFactory.CreateInMemoryContext();
            TaskRepository taskRepo = new(context, Substitute.For<INotificationService>());

            var task = new TaskItemBuilder()
                .WithId(500)
                .WithTitle("Rapid Click Task")
                .WithRecurrence(RecurrenceType.Daily)
                .WithScheduledDate(TodoDay.Today.ToDateTime())
                .WithStatus(TaskStatus.Planned)
                .Build();

            taskRepo.Add(task);

            // Act: Rapid double click on complete task
            taskRepo.CompleteTask(task.Id);
            taskRepo.CompleteTask(task.Id);

            var copies = taskRepo.GetAll().Where(t => t.RecurrenceSourceId == task.Id).ToList();

            // Assert: Only single recurring copy generated in repository
            copies.Should().HaveCount(1);
        }
    }

    /*[Fact]
    public void RetroactiveCompletion_OfRecurringTask_ValidatesEntireFlowAndDeepCopy()
    {
        // Arrange: Завершение повтор-задачи "задним числом" должно корректно переносить свойства и вычислять дату
        using var context = TestDbContextFactory.CreateInMemoryContext();
        var taskRepo = new TaskRepository(context, Substitute.For<INotificationService>());

        var twoDaysAgo = TodoDay.Today.ToDateTime().AddDays(-2);

        // Создаем ежедневную задачу с подзадачей, которая была запланирована на позавчера
        var subtask = new TaskItemBuilder().WithId(402).WithTitle("Subtask to copy").Build();
        var task = new TaskItemBuilder()
            .WithId(401)
            .WithTitle("Retroactive Recurring Task")
            .WithRecurrence(RecurrenceType.Daily)
            .WithScheduledDate(twoDaysAgo, DateSource.AutoFixed)
            .WithStatus(TaskStatus.Planned)
            .WithSubtask(subtask)
            .Build();

        taskRepo.Add(task);

        // Act: Принудительно завершаем задачу позавчерашним днем
        taskRepo.CompleteTask(task.Id, completionDate: twoDaysAgo);

        // Assert: Проверяем старую задачу
        var oldTask = taskRepo.GetById(task.Id);
        oldTask!.Status.Should().Be(TaskStatus.Completed);
        oldTask.CompletedDate.Should().Be(twoDaysAgo);

        // Assert: Проверяем новую задачу (весь флоу)
        var newCopy = taskRepo.GetAll().FirstOrDefault(t => t.RecurrenceSourceId == task.Id);

        // 1. Факт создания
        newCopy.Should().NotBeNull("Экземпляр-повтор должен быть создан при завершении задним числом");

        // 2. Статус и связь
        newCopy!.Status.Should().Be(TaskStatus.Planned, "Новая задача должна быть в статусе Planned");
        newCopy.DateSource.Should().Be(DateSource.AutoFixed, "Источник даты повторений - AutoFixed");

        // 3. Вычисление даты: раз она ежедневная и завершена позавчера (-2 дня), 
        // следующий повтор должен быть запланирован на вчера (-1 день).
        var expectedNextDate = twoDaysAgo.AddDays(1);
        newCopy.ScheduledDate.Should().Be(expectedNextDate,
            "Новая дата должна рассчитываться от даты завершения (позавчера), а не от сегодня");

        // 4. Глубокое копирование атрибутов повторения
        newCopy.IsRecurring.Should().BeTrue();
        newCopy.RecurrenceType.Should().Be(RecurrenceType.Daily);

        // 5. Глубокое копирование подзадач
        newCopy.Subtasks.Should().HaveCount(1, "Подзадачи должны копироваться в новый экземпляр");
        newCopy.Subtasks.First().Title.Should().Be("Subtask to copy");
    }*/
}