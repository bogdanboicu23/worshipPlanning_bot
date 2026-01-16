using Microsoft.EntityFrameworkCore;
using WorshipPlannerBot.Api.Models;

namespace WorshipPlannerBot.Api.Data;

public class BotDbContext : DbContext
{
    public BotDbContext(DbContextOptions<BotDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<Attendance> Attendances { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.TelegramId)
            .IsUnique();

        modelBuilder.Entity<UserRole>()
            .HasKey(ur => new { ur.UserId, ur.RoleId });

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId);

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId);

        modelBuilder.Entity<Attendance>()
            .HasKey(a => new { a.EventId, a.UserId });

        modelBuilder.Entity<Attendance>()
            .HasOne(a => a.Event)
            .WithMany(e => e.Attendances)
            .HasForeignKey(a => a.EventId);

        modelBuilder.Entity<Attendance>()
            .HasOne(a => a.User)
            .WithMany(u => u.Attendances)
            .HasForeignKey(a => a.UserId);

        modelBuilder.Entity<Event>()
            .HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        SeedRoles(modelBuilder);
    }

    private void SeedRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = "Vocals", Icon = "🎤", DisplayOrder = 1, Description = "Lead and backing vocals" },
            new Role { Id = 2, Name = "Guitar", Icon = "🎸", DisplayOrder = 2, Description = "Acoustic and electric guitar" },
            new Role { Id = 3, Name = "Bass", Icon = "🎸", DisplayOrder = 3, Description = "Bass guitar" },
            new Role { Id = 4, Name = "Drums", Icon = "🥁", DisplayOrder = 4, Description = "Drums and percussion" },
            new Role { Id = 5, Name = "Keyboard", Icon = "🎹", DisplayOrder = 5, Description = "Piano and keyboards" },
            new Role { Id = 6, Name = "Sound Tech", Icon = "🎧", DisplayOrder = 6, Description = "Sound mixing and audio" },
            new Role { Id = 7, Name = "Media", Icon = "📹", DisplayOrder = 7, Description = "Visuals and streaming" },
            new Role { Id = 8, Name = "Prayer", Icon = "🙏", DisplayOrder = 8, Description = "Prayer team" }
        );
    }
}