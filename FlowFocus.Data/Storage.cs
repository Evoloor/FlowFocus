using TaskStatus = FlowFocus.Core.Enums.TaskStatus;
using FlowFocus.Core;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace FlowFocus.Data;
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
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
public abstract class BaseRepository<T>(AppDbContext context) : IRepository<T>
    where T : class
{
    public virtual async Task<T?> GetByIdAsync(Guid id)
    {
        return await context.Set<T>().FindAsync(id);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        return await context.Set<T>().ToListAsync();
    }

    public virtual async Task AddAsync(T entity)
    {
        await context.Set<T>().AddAsync(entity);
        await context.SaveChangesAsync();
    }

    public virtual async Task UpdateAsync(T entity)
    {
        context.Set<T>().Update(entity);
        await context.SaveChangesAsync();
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            context.Set<T>().Remove(entity);
            await context.SaveChangesAsync();
        }
    }

    public virtual async Task<bool> ExistsAsync(Guid id)
    {
        return await context.Set<T>().FindAsync(id) != null;
    }
}
public class DependencyRepository(AppDbContext context) : BaseRepository<Dependency>(context), IDependencyRepository
{
    private readonly AppDbContext _context = context;

    public async Task<IEnumerable<Dependency>> GetDependenciesForTaskAsync(Guid taskId)
    {
        return await _context.Dependencies
            .Where(d => d.SourceTaskId == taskId)
            .Include(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<Dependency>> GetDependentTasksAsync(Guid taskId)
    {
        return await _context.Dependencies
            .Where(d => d.TargetTaskId == taskId)
            .Include(d => d.SourceTask)
            .ToListAsync();
    }

    public async Task<bool> HasCircularDependencyAsync(Guid sourceTaskId, Guid targetTaskId)
    {
        return await _context.Dependencies.AnyAsync(d => 
            d.SourceTaskId == targetTaskId && d.TargetTaskId == sourceTaskId);
    }

    public async Task RemoveDependenciesForTaskAsync(Guid taskId)
    {
        var dependencies = await _context.Dependencies
            .Where(d => d.SourceTaskId == taskId)
            .ToListAsync();
            
        _context.Dependencies.RemoveRange(dependencies);
        await _context.SaveChangesAsync();
    }
}
public class SettingsRepository(AppDbContext context) : BaseRepository<UserSettings>(context), ISettingsRepository
{
    private readonly AppDbContext _context = context;

    public async Task<UserSettings> GetUserSettingsAsync()
    {
        var settings = await _context.UserSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new UserSettings();
            await _context.UserSettings.AddAsync(settings);
            await _context.SaveChangesAsync();
        }
        return settings;
    }
}
public static class ServiceExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services)
    {
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IDependencyRepository, DependencyRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        
        return services;
    }
}
public class TaskRepository(AppDbContext context) : BaseRepository<TaskItem>(context), ITaskRepository
{
    private readonly AppDbContext _context = context;

    public async Task<IEnumerable<TaskItem>> GetByStatusAsync(TaskStatus status)
    {
        return await _context.Tasks
            .Where(t => t.Status == status)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<TaskItem>> GetByDateAsync(DateTime date)
    {
        return await _context.Tasks
            .Where(t => t.PlannedDate.HasValue && 
                       t.PlannedDate.Value.Date == date.Date)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<TaskItem>> GetByPriorityRangeAsync(int minPriority, int maxPriority)
    {
        return await _context.Tasks
            .Where(t => t.UserPriority >= minPriority && t.UserPriority <= maxPriority)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<TaskItem>> GetWithDependenciesAsync()
    {
        return await _context.Tasks
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<TaskItem>> GetNotConfiguredAsync()
    {
        return await _context.Tasks
            .Where(t => t.Status == TaskStatus.NotConfigured)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task UpdateStatusAsync(Guid taskId, TaskStatus status)
    {
        var task = await _context.Tasks.FindAsync(taskId);
        if (task != null)
        {
            task.Status = status;
            await _context.SaveChangesAsync();
        }
    }
}
public class AppDbContextFactory(IServiceProvider serviceProvider) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        return serviceProvider.GetRequiredService<AppDbContext>();
    }
}
