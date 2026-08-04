using FlowFocus.Data;
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
}
