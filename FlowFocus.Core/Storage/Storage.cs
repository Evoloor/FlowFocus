using System.Text.Json;
using Microsoft.JSInterop;

namespace FlowFocus.Core.Storage;

public interface IStorageService<T>
{
    Task<T?> LoadAsync();
    Task SaveAsync(T data);
    Task ClearAsync();
}

public class FileStorageService<T> : IStorageService<T>
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public FileStorageService(string? fileName = null)
    {
        fileName ??= AppSettings.FileName;
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(folder))
            folder = ".";
        _filePath = Path.Combine(folder, fileName);
    }

    public async Task<T?> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return default;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<T>(json, _options);
        }
        catch
        {
            return default;
        }
    }

    public async Task SaveAsync(T data)
    {
        var json = JsonSerializer.Serialize(data, _options);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public Task ClearAsync()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
        return Task.CompletedTask;
    }
}

public class LocalStorageService<T> : IStorageService<T>
{
    private readonly string _key;
    private readonly IJSRuntime _js;
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public LocalStorageService(IJSRuntime js, string key)
    {
        _js = js;
        _key = key;
    }

    public async Task<T?> LoadAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string>("localStorage.getItem", _key);
            if (string.IsNullOrWhiteSpace(json)) return default;
            return JsonSerializer.Deserialize<T>(json, _options);
        }
        catch
        {
            return default;
        }
    }

    public async Task SaveAsync(T data)
    {
        var json = JsonSerializer.Serialize(data, _options);
        await _js.InvokeVoidAsync("localStorage.setItem", _key, json);
    }

    public async Task ClearAsync()
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", _key);
    }
}