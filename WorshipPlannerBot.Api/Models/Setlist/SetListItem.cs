using System.ComponentModel.DataAnnotations;

namespace WorshipPlannerBot.Api.Models.Setlist;

public class SetListItem
{
    [Key]
    public int Id { get; set; }
    
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;

    public int? SongId {get; set;}
    public Song? Song { get; set; }
    
    public int OrderIndex { get; set; }
    
    [MaxLength(100)]
    public string? SpecialKey { get; set; }
    
    [MaxLength(200)]
    public string? CustomTitle { get; set; }
    
    [MaxLength(500)]
    public string? Notes { get; set; }

    public SetListItemType ItemType { get; set; } = SetListItemType.Song;

}

public enum SetListItemType
{
    Song,
    Prayer,
    Scripture,
    Announcement,
    Welcome,
    Offering,
    Communion,
    Other
}