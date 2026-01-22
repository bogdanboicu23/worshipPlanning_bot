using System.ComponentModel.DataAnnotations;

namespace WorshipPlannerBot.Api.Models.Setlist;

public class Song
{
    [Key]
    public int Id { get; set; }

    [Required] [MaxLength(200)] 
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(200)] 
    public string Artist { get; set; } = string.Empty;
    
    [MaxLength(200)] 
    public string Key { get; set;  } = string.Empty;  // Tonalitate
    
    [MaxLength(500)] 
    public string Tempo { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string YoutubeUrl { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string SpotifyUrl { get; set; } = string.Empty;
    
    [MaxLength(500)] 
    public string ChordSheetUrl { get; set; } = string.Empty;
    
    public string? Lyrics { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<SetListItem> SetListItems { get; set; } = new List<SetListItem>();
    public ICollection<ChordChart> ChordCharts { get; set; } = new List<ChordChart>();
}