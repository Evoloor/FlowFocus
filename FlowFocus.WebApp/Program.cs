using FlowFocus.Core;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using FlowFocus.Blazor.Layout;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
_ = builder.Services.AddMudServices()
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// Кастомный импорт сервисов
_ = builder.Services.AddDataLayer();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<FlowFocus.WebApp.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(MainLayout).Assembly);

// Кастомное заполнение БД
app.Services.InitializeDatabase();

app.Run();


internal static class ServiceExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services)
    {
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

    public static void InitializeDatabase(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StorageContext>();

        // Автоматическое создание БД и применение миграций
        context.Database.Migrate();
    }
}
