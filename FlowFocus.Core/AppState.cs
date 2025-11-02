using FlowFocus.Core.Models;
using FlowFocus.Core.Storage;
using Microsoft.JSInterop;

namespace FlowFocus.Core;

public static class AppSettings
{
    public static string FileName => "flowfocus.json";
}

public class AppState
{
    public DateTime CurrentDay { get; set; } = DateTime.Today;
    public List<TaskItem> Tasks { get; set; } = new();
    public UserAppSettings Settings { get; set; } = new();
}

public class AppStateManager
{
    public static AppStateManager Shared { get; set; } = new();

    public virtual IStorageService<AppState>? Storage { get; set; }
    public AppState State { get; private set; } = new AppState();

    public AppStateManager()
    {
    }

    /// <summary>Инициализация для WASM (LocalStorage) или десктопа (файл).</summary>
    /*public void Init(IJSRuntime? js = null)
    {
#if BLAZOR_WEBASSEMBLY
            if (js == null) throw new InvalidOperationException("Для WASM нужно передать IJSRuntime");
            _storage = new Storage.LocalStorageService<AppState>(js, AppSettings.FileName);
#else
        _storage = new Storage.FileStorageService<AppState>(AppSettings.FileName);
#endif
    }*/

    /// <summary>Загрузить state из storage</summary>
    public async Task LoadAsync()
    {
        if (Storage == null) throw new InvalidOperationException("AppStateManager не инициализирован");
        var loaded = await Storage.LoadAsync();
        if (loaded != null) State = loaded;
    }

    /// <summary>Сохранить state в storage</summary>
    public async Task SaveAsync()
    {
        if (Storage == null) throw new InvalidOperationException("AppStateManager не инициализирован");
        await Storage.SaveAsync(State);
    }

    public async Task ClearAsync()
    {
        State = new AppState();
        if (Storage != null) await Storage.ClearAsync();
    }

    public async Task AddTaskAsync(TaskItem task)
    {
        State.Tasks.Add(task);
        await SaveAsync();
    }

    public async Task UpdateTaskAsync(TaskItem task)
    {
        var idx = State.Tasks.FindIndex(t => t.Id == task.Id);
        if (idx >= 0) State.Tasks[idx] = task;
        await SaveAsync();
    }

    public async Task RemoveTaskAsync(TaskItem task)
    {
        State.Tasks.Remove(task);
        await SaveAsync();
    }
}