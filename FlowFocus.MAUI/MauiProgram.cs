using Microsoft.Extensions.Logging;
using FlowFocus.Core;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

namespace FlowFocus.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Настройка пути к БД для разных платформ
        var dbPath = GetDatabasePath();
        
        // Настройка сервисов
        _ = builder.Services.AddDataLayer(dbPath);
        
        // Инициализация БД
        var app = builder.Build();
        app.Services.InitializeDatabase();
        
        return app;
    }

    private static string GetDatabasePath()
    {
        const string dbName = "flowfocus.db";
        
#if ANDROID
        var path = Path.Combine(FileSystem.AppDataDirectory, dbName);
#elif IOS || MACCATALYST
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "..", "Library", dbName);
#elif WINDOWS
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowFocus", dbName);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
#else
        var path = dbName;
#endif
        
        return path;
    }
}

internal static class ServiceExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services, string dbPath)
    {
        services.AddDbContext<StorageContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Сервис уведомлений (singleton для broadcast между компонентами)
        services.AddSingleton<INotificationService, NotificationService>();

        // Репозитории
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IPriorityRepository, PriorityRepository>();
        services.AddScoped<ITagRepository, TagRepository>();

        // Сервисы
        services.AddScoped<IPlannerService, PlannerService>();
        services.AddScoped<ITagSessionService, TagSessionService>();

        return services;
    }

    public static void InitializeDatabase(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StorageContext>();

        // Автоматическое создание БД и применение миграций
        context.Database.Migrate();
    }
}