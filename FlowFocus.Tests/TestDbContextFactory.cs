using FlowFocus.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Tests;

public static class TestDbContextFactory
{
    public static StorageContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        StorageContext context = new(options);
        context.Database.EnsureCreated();
        return context;
    }
}
