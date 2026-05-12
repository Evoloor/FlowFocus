namespace FlowFocus.Core.Services;

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




