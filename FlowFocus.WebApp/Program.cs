using FlowFocus.Core;
using FlowFocus.Data;
using FlowFocus.WebApp;
using MudBlazor.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>();

// Business Logic Services
builder.Services.AddScoped<IPlannerService, BasicPlannerService>();

// Register repositories
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<IDependencyRepository, DependencyRepository>();
builder.Services.AddScoped<ISettingsRepository, SettingsRepository>();

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

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
    // Автоматическое создание БД и применение миграций
    await context.Database.MigrateAsync();
    
    // Ensure default settings exist
    var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
    await settingsRepo.GetUserSettingsAsync();
}

app.Run();
