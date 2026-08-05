using FlowFocus.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Tests;

public static class TestDbContextFactory
{
    public static StorageContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        StorageContext context = new(options: options);
        context.Database.EnsureCreated();
        return context;
    }

    public static (StorageContext Context, SqliteConnection Connection) CreateSqliteContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseSqlite(connection)
            .Options;

        StorageContext context = new(options: options);
        context.Database.EnsureCreated();
        return (context, connection);
    }
}
