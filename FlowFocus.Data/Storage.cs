using System.Text.Json;
using FlowFocus.Core;
using FlowFocus.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace FlowFocus.Data;

public class StorageContext : DbContext
{
    private readonly string _dbPath;

    public StorageContext(string? dbPath = null)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(folder))
            folder = ".";
        _dbPath = Path.Combine(folder, dbPath ?? "flowfocus.db");
    }

    public DbSet<StorageEntry> Entries => Set<StorageEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");
}

public class StorageEntry
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public enum StorageKey
{
    UserSettings,
    SessionState,
    Tasks,
    Preferences
}

public static class StorageKeys
{
    public static readonly Dictionary<StorageKey, string> Map = new()
    {
        { StorageKey.UserSettings, nameof(StorageKey.UserSettings) },
        { StorageKey.SessionState, nameof(StorageKey.SessionState) },
        { StorageKey.Tasks, nameof(StorageKey.Tasks) },
        { StorageKey.Preferences, nameof(StorageKey.Preferences) },
    };
}

public class DbStorageService<T>(StorageKey key) : IStorageService<T>
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<T?> LoadAsync()
    {
        await using var db = new StorageContext();
        await db.Database.EnsureCreatedAsync();
        var entry = await db.Entries.FirstOrDefaultAsync(e => e.Key == StorageKeys.Map[key]);
        if (entry == null) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(entry.Value, Options);
        }
        catch
        {
            return default;
        }
    }

    public async Task SaveAsync(T data)
    {
        await using var db = new StorageContext();
        await db.Database.EnsureCreatedAsync();
        var json = JsonSerializer.Serialize(data, Options);
        var entry = await db.Entries.FirstOrDefaultAsync(e => e.Key == StorageKeys.Map[key]);
        if (entry == null)
        {
            db.Entries.Add(new StorageEntry { Key = StorageKeys.Map[key], Value = json });
        }
        else
        {
            entry.Value = json;
            db.Entries.Update(entry);
        }

        await db.SaveChangesAsync();
    }

    public async Task ClearAsync()
    {
        await using var db = new StorageContext();
        var entry = await db.Entries.FirstOrDefaultAsync(e => e.Key == StorageKeys.Map[key]);
        if (entry != null)
        {
            db.Entries.Remove(entry);
            await db.SaveChangesAsync();
        }
    }
}

public class FileStorageService<T> : IStorageService<T>
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return default;
        }
    }

    public async Task SaveAsync(T data)
    {
        var json = JsonSerializer.Serialize(data, Options);
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
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return default;
        }
    }

    public async Task SaveAsync(T data)
    {
        var json = JsonSerializer.Serialize(data, Options);
        await _js.InvokeVoidAsync("localStorage.setItem", _key, json);
    }

    public async Task ClearAsync()
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", _key);
    }
}