using FlowFocus.Core.Models;

namespace FlowFocus.Core;

/// <summary>
/// Репозиторий настроек пользователя
/// </summary>
public interface ISettingsRepository : IRepository<UserSettings>
{
    UserSettings GetUserSettings();
    void UpdateSettings(UserSettings settings);
}