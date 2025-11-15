using FlowFocus.Core;
using FlowFocus.Data;
using MudBlazor.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();

// Data Layer - правильная регистрация DbContext и фабрики
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=flowfocus.db"));

// Регистрируем фабрику DbContext для репозиториев
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite("Data Source=flowfocus.db"));

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
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

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