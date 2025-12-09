using FlowFocus.Data;
using FlowFocus.WebApp;
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
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Кастомное заполнение БД
app.Services.InitializeDatabase();

app.Run();


internal static class ServiceExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services)
    {
        services.AddDbContext<StorageContext>();
        
        // Репозитории

        // Сервисы

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
