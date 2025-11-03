using FlowFocus.Core.Models;
using FlowFocus.Data;
using MudBlazor.Services;
using FlowFocus.WebApp.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<StorageContext>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<IRepository<UserAppSettings>, SettingsRepository>();

var app = builder.Build();

// Автоматические миграции при запуске
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<StorageContext>();
    context.Database.Migrate(); // Самое главное - применяем миграции
    
    // Создаем настройки по умолчанию если их нет
    if (!context.Settings.Any())
    {
        context.Settings.Add(new UserAppSettings());
        context.SaveChanges();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.Run();
