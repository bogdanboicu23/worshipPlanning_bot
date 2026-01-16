using System.ComponentModel.DataAnnotations;

namespace WorshipPlannerBot.Api.Models;

public class Role
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Icon { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}