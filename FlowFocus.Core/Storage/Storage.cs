namespace FlowFocus.Core.Storage;

public interface IStorageService<T>
{
    Task<T?> LoadAsync();
    Task SaveAsync(T data);
    Task ClearAsync();
}