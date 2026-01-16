using System.ComponentModel.DataAnnotations;

namespace WorshipPlannerBot.Api.Models;

public class Event
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public DateTime DateTime { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public bool IsCancelled { get; set; }

    public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
}