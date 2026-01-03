using FlowFocus.Data;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;

Console.WriteLine("Starting recurrence tester...");

var options = new DbContextOptionsBuilder<StorageContext>()
    .UseSqlite("Data Source=recurrence_test.db")
    .Options;

using var ctx = new StorageContext(options);
ctx.Database.EnsureDeleted();
ctx.Database.EnsureCreated();

var repo = new TaskRepository(ctx);

var task = new TaskItem
{
    Title = "Daily task",
    Status = FlowFocus.Core.Enums.TaskStatus.Planned,
    IsRecurring = true,
    RecurrenceType = FlowFocus.Core.Enums.RecurrenceType.Daily,
    UserAssignedDate = DateTime.UtcNow.Date,
    ActualAssignedDate = DateTime.UtcNow.Date
};

repo.Add(task);
Console.WriteLine($"Created task id={task.Id}");

try
{
    repo.CompleteTask(task.Id);
    Console.WriteLine("CompleteTask finished");
}
catch (Exception ex)
{
    Console.WriteLine("Exception: " + ex);
}

var all = repo.GetAll();
Console.WriteLine($"All tasks count: {all.Count}");
foreach (var t in all)
{
    Console.WriteLine($"Task {t.Id}: {t.Title}, status={t.Status}, userDate={t.UserAssignedDate}, source={t.RecurrenceSourceId}");
}

Console.WriteLine("Done");
