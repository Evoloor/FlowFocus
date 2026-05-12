using FlowFocus.Blazor.Layout;
using FlowFocus.Data;
using FlowFocus.WebApp;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
_ = builder.Services.AddMudServices()
    .AddRazorComponents()
    .AddInteractiveServerComponents();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(MainLayout).Assembly);

// Кастомное заполнение БД
app.Services.InitializeDatabase();

app.Run();