using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data.Repositories;

/// <summary>
/// Репозиторий настроек
/// </summary>
public class SettingsRepository(StorageContext context, INotificationService notificationService) : CachedRepository<UserSettings>(context, notificationService), ISettingsRepository
{
    protected override DbSet<UserSettings> GetDbSet() => Context.Settings;

    public UserSettings GetUserSettings()
    {
        var settings = GetAll().FirstOrDefault();
        if (settings == null)
        {
            settings = new() { Id = 1 };
            Add(settings);
        }
        return settings;
    }

    public void UpdateSettings(UserSettings settings)
    {
        Update(settings);
        TodoDay.Configure(settings.DayStartHour);
    }
}