using TaskStatus = FlowFocus.Core.Enums.TaskStatus;
using FlowFocus.Core;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace FlowFocus.Data;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TaskItem> Tasks { get; set; }
    public DbSet<Dependency> Dependencies { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TaskItem configuration
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Title).IsRequired().HasMaxLength(500);
            entity.Property(t => t.Description).HasMaxLength(2000);
            entity.Property(t => t.Tags).HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            );
            entity.Property(t => t.Status).HasConversion<string>();
            entity.Property(t => t.DisplayType).HasConversion<string>();
            
            // Indexes for performance
            entity.HasIndex(t => t.Status);
            entity.HasIndex(t => t.PlannedDate);
            entity.HasIndex(t => t.UserPriority);
        });

        // Dependency configuration
        modelBuilder.Entity<Dependency>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Type).HasConversion<string>();
            entity.Property(d => d.Logic).HasConversion<string>();
            
            // Relationships
            entity.HasOne(d => d.SourceTask)
                  .WithMany(t => t.Dependencies)
                  .HasForeignKey(d => d.SourceTaskId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(d => d.TargetTask)
                  .WithMany(t => t.DependentTasks)
                  .HasForeignKey(d => d.TargetTaskId)
                  .OnDelete(DeleteBehavior.Restrict);
            
            // Prevent self-references
            entity.HasCheckConstraint("CK_Dependency_SelfReference", "SourceTaskId != TargetTaskId");
        });

        // UserSettings configuration
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasData(new UserSettings { Id = Guid.NewGuid() });
        });
    }
}
public abstract class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    protected BaseRepository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id) => await _dbSet.FindAsync(id);
    public virtual async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();
    public virtual async Task AddAsync(T entity) => await _dbSet.AddAsync(entity);
    public virtual Task UpdateAsync(T entity) => Task.FromResult(_dbSet.Update(entity));
    public virtual Task DeleteAsync(Guid id) => Task.Run(async () => 
    {
        var entity = await GetByIdAsync(id);
        if (entity != null) _dbSet.Remove(entity);
    });
    public virtual async Task<bool> ExistsAsync(Guid id) => await _dbSet.FindAsync(id) != null;
}
public class DependencyRepository : BaseRepository<Dependency>, IDependencyRepository
{
    public DependencyRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<Dependency>> GetDependenciesForTaskAsync(Guid taskId)
        => await _dbSet.Where(d => d.SourceTaskId == taskId)
                       .Include(d => d.TargetTask)
                       .ToListAsync();

    public async Task<IEnumerable<Dependency>> GetDependentTasksAsync(Guid taskId)
        => await _dbSet.Where(d => d.TargetTaskId == taskId)
                       .Include(d => d.SourceTask)
                       .ToListAsync();

    public async Task<bool> HasCircularDependencyAsync(Guid sourceTaskId, Guid targetTaskId)
    {
        // Simple implementation - can be enhanced with graph traversal
        return await _dbSet.AnyAsync(d => 
            d.SourceTaskId == targetTaskId && d.TargetTaskId == sourceTaskId);
    }

    public async Task RemoveDependenciesForTaskAsync(Guid taskId)
    {
        var dependencies = await GetDependenciesForTaskAsync(taskId);
        _dbSet.RemoveRange(dependencies);
        await _context.SaveChangesAsync();
    }
}
public class SettingsRepository : BaseRepository<UserSettings>, ISettingsRepository
{
    public SettingsRepository(AppDbContext context) : base(context) { }

    public async Task<UserSettings> GetUserSettingsAsync()
    {
        var settings = await _dbSet.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new UserSettings();
            await AddAsync(settings);
            await _context.SaveChangesAsync();
        }
        return settings;
    }
}

public static class ServiceExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(connectionString));
            
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IDependencyRepository, DependencyRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        
        return services;
    }
}
public class TaskRepository : BaseRepository<TaskItem>, ITaskRepository
{
    public TaskRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<TaskItem>> GetByStatusAsync(TaskStatus status)
        => await _dbSet.Where(t => t.Status == status).ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetByDateAsync(DateTime date)
        => await _dbSet.Where(t => t.PlannedDate.HasValue && 
                              t.PlannedDate.Value.Date == date.Date).ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetByPriorityRangeAsync(int minPriority, int maxPriority)
        => await _dbSet.Where(t => t.UserPriority >= minPriority && t.UserPriority <= maxPriority).ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetWithDependenciesAsync()
        => await _dbSet.Include(t => t.Dependencies).ThenInclude(d => d.TargetTask).ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetNotConfiguredAsync()
        => await _dbSet.Where(t => t.Status == TaskStatus.NotConfigured).ToListAsync();

    public async Task UpdateStatusAsync(Guid taskId, TaskStatus status)
    {
        var task = await GetByIdAsync(taskId);
        if (task != null)
        {
            task.Status = status;
            await _context.SaveChangesAsync();
        }
    }
}
public class AppDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly IServiceProvider _serviceProvider;

    public AppDbContextFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public AppDbContext CreateDbContext()
    {
        return _serviceProvider.GetRequiredService<AppDbContext>();
    }
}
