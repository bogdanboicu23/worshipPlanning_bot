using System.ComponentModel.DataAnnotations;

namespace WorshipPlannerBot.Api.Models;

public class Attendance
{
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [Required]
    public AttendanceStatus Status { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Note { get; set; }
}

public enum AttendanceStatus
{
    Yes = 1,
    No = 2,
    Maybe = 3
}