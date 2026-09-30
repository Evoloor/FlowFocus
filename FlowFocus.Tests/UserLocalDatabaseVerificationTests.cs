using FlowFocus.Core;
using FlowFocus.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowFocus.Tests;

public class UserLocalDatabaseVerificationTests
{
    [Fact]
    public void CheckUserLocalDatabase_IfPresent()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userDbPath = Path.Combine(localAppData, "FlowFocus", "flowfocus.db");

        if (!File.Exists(userDbPath))
        {
            return; // Not on this machine or no local db
        }

        var services = new ServiceCollection();
        services.AddDataLayer(userDbPath);

        using var sp = services.BuildServiceProvider();
        
        // Let's test InitializeDatabase on the actual user database!
        var act = () => sp.InitializeDatabase();
        act.Should().NotThrow("User's local flowfocus.db must migrate cleanly without exceptions");

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StorageContext>();
        
        // Query tasks and settings
        var tasks = context.Tasks.Take(10).ToList();
        var settings = context.Settings.FirstOrDefault();
        settings.Should().NotBeNull();
    }
}
