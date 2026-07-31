using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FlowFocus.Core;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;

namespace FlowFocus.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services)
    {
        // Регистрация DbContext с конфигурацией по умолчанию (StorageContext.OnConfiguring)
        services.AddDbContext<StorageContext>();

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

    public static IServiceCollection AddDataLayer(this IServiceCollection services, string dbPath)
    {
        services.AddDbContext<StorageContext>(options => options.UseSqlite($"Data Source={dbPath}"));

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

        // Инициализация TodoDay настройкой времени начала дня
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        TodoDay.Configure(settingsRepo.GetUserSettings().DayStartHour);
    }
}
