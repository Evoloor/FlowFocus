using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data;

public class StorageContext : DbContext
{
    public DbSet<TaskItem> Tasks { get; set; } = null!;
    public DbSet<PriorityLevel> Priorities { get; set; } = null!;
    public DbSet<Tag> Tags { get; set; } = null!;
    public DbSet<TaskTag> TaskTags { get; set; } = null!;
    public DbSet<TaskRelation> TaskRelations { get; set; } = null!;
    public DbSet<PriorityEscalation> PriorityEscalations { get; set; } = null!;
    public DbSet<UserSettings> Settings { get; set; } = null!;

    public StorageContext()
    {
    }

    public StorageContext(DbContextOptions<StorageContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=flowfocus.db");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TaskItem
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.ParentTask)
                .WithMany(e => e.Subtasks)
                .HasForeignKey(e => e.ParentTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Priority)
                .WithMany()
                .HasForeignKey(e => e.PriorityId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Tags)
                .WithOne(e => e.Task)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Relations)
                .WithOne(e => e.SourceTask)
                .HasForeignKey(e => e.SourceTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.InverseRelations)
                .WithOne(e => e.TargetTask)
                .HasForeignKey(e => e.TargetTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.PriorityEscalations)
                .WithOne(e => e.Task)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // TaskRelation
        modelBuilder.Entity<TaskRelation>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // TaskTag
        modelBuilder.Entity<TaskTag>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Tag)
                .WithMany()
                .HasForeignKey(e => e.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PriorityLevel
        modelBuilder.Entity<PriorityLevel>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // Tag
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // PriorityEscalation
        modelBuilder.Entity<PriorityEscalation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.TargetPriority)
                .WithMany()
                .HasForeignKey(e => e.TargetPriorityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserSettings
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // Seed default priorities
        modelBuilder.Entity<PriorityLevel>().HasData(
            new PriorityLevel { Id = 1, Order = 1, Name = "Критический", Color = "#FF4444" },
            new PriorityLevel { Id = 2, Order = 2, Name = "Высокий", Color = "#FF8C00" },
            new PriorityLevel { Id = 3, Order = 3, Name = "Средний", Color = "#FFD700" },
            new PriorityLevel { Id = 4, Order = 4, Name = "Низкий", Color = "#4CAF50" },
            new PriorityLevel { Id = 5, Order = 5, Name = "Фоновый", Color = "#2196F3" }
        );

        // Seed default settings
        modelBuilder.Entity<UserSettings>().HasData(
            new UserSettings
            {
                Id = 1,
                DayStartHour = 5,
                DailyComplexityLimit = 100,
                DailyTimeLimit = 480,
                DailyTaskLimit = 10,
                AutoDistributeEnabled = true,
                IsDarkMode = true,
                HideTaskTitlesDefault = false,
                DefaultPriorityId = 3
            }
        );
    }
}