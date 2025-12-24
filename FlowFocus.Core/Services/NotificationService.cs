namespace FlowFocus.Core.Services;

/// <summary>
/// Сервис уведомлений об изменении данных для реактивного обновления UI
/// </summary>
public interface INotificationService
{
    event Action? OnTasksChanged;
    event Action? OnSettingsChanged;
    
    void NotifyTasksChanged();
    void NotifySettingsChanged();
}

public class NotificationService : INotificationService
{
    public event Action? OnTasksChanged;
    public event Action? OnSettingsChanged;

    public void NotifyTasksChanged()
    {
        OnTasksChanged?.Invoke();
    }

    public void NotifySettingsChanged()
    {
        OnSettingsChanged?.Invoke();
    }
}




