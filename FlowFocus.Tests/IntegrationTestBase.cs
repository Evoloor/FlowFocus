using FlowFocus.Core.Services;
using FlowFocus.Data;
using FlowFocus.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace FlowFocus.Tests;

/// <summary>
/// Abstract base class for integration and database tests.
/// Manages in-memory DbContext lifecycle and exposes core repositories and services.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    protected StorageContext Context { get; }
    protected INotificationService NotificationService { get; }
    protected TaskRepository TaskRepo { get; }
    protected PriorityRepository PriorityRepo { get; }
    protected TagRepository TagRepo { get; }
    protected ExternalConditionRepository ConditionRepo { get; }
    protected SettingsRepository SettingsRepo { get; }
    protected PlannerService PlannerService { get; }

    protected IntegrationTestBase()
    {
        (Context, _sqliteConnection) = TestDbContextFactory.CreateSqliteContext();
        NotificationService = new NotificationService();
        TaskRepo = new(Context, NotificationService);
        PriorityRepo = new(Context, NotificationService);
        TagRepo = new(Context, NotificationService);
        ConditionRepo = new(Context, NotificationService);
        SettingsRepo = new(Context, NotificationService);
        PlannerService = new(TaskRepo);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Context.Dispose();
            _sqliteConnection.Dispose();
        }
    }
}
