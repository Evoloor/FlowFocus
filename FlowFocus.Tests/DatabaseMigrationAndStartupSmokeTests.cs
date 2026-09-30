using FlowFocus.Core;
using FlowFocus.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowFocus.Tests;

/// <summary>
/// Smoke-тесты целостности схемы БД, наличия незафиксированных миграций и процесса запуска приложения.
/// Гарантирует, что любые изменения моделей данных сопровождаются миграцией EF Core
/// и метод InitializeDatabase() не выбрасывает исключений при старте.
/// </summary>
public class DatabaseMigrationAndStartupSmokeTests
{
    [Fact]
    public void StorageContext_ShouldHaveNoPendingModelChanges()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new StorageContext(options);

        // Act & Assert (EF Core 9+)
        var hasPendingChanges = context.Database.HasPendingModelChanges();
        hasPendingChanges.Should().BeFalse(
            "в моделях данных присутствуют изменения, для которых не создана миграция EF Core. " +
            "Выполните 'dotnet ef migrations add <Name> --project FlowFocus.Data --startup-project FlowFocus.WebApp'");
    }

    [Fact]
    public void ApplicationStartup_InitializeDatabase_ExecutesCleanly()
    {
        // Arrange: Создаем временный файл SQLite для имитации реального запуска приложения
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"flowfocus_smoke_{Guid.NewGuid():N}.db");

        try
        {
            var services = new ServiceCollection();
            services.AddDataLayer(tempDbPath);

            using var serviceProvider = services.BuildServiceProvider();

            // Act: Выполняем ровно то, что делает WebApp Program.cs при старте
            var act = () => serviceProvider.InitializeDatabase();

            // Assert: Приложение должно успешно применить миграции и инициализировать TodoDay
            act.Should().NotThrow("инициализация базы данных при запуске приложения должна проходить без ошибок");

            // Проверяем, что настройки загрузились и TodoDay сконфигурирован
            var settingsRepo = serviceProvider.GetRequiredService<ISettingsRepository>();
            var settings = settingsRepo.GetUserSettings();
            settings.Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { /* ignore */ }
            }
        }
    }
}
