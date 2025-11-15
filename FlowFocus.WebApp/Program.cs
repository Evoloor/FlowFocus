using FlowFocus.Core;
using FlowFocus.Data;
using FlowFocus.WebApp;
using MudBlazor.Services;
using Microsoft.EntityFrameworkCore;

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
_ = app.Services.InitializeDatabaseAsync();

app.Run();
