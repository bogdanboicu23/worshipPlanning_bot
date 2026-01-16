using System.ComponentModel.DataAnnotations;

namespace WorshipPlannerBot.Api.Models;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    public long TelegramId { get; set; }

    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(50)]
    public string? Username { get; set; }

    public bool IsAdmin { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastActiveAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
}