namespace FlowFocus.Core.Storage;
public interface ITaskRepository<T>
{
    Task<List<T>> GetAllAsync();
    Task<T?> GetByIdAsync(int id);
    Task AddAsync(T task);
    Task UpdateAsync(T task);
    Task DeleteAsync(int id);
}
