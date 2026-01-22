using System.ComponentModel.DataAnnotations;

namespace WorshipPlannerBot.Api.Models.Setlist;

public class ChordChart
{
    [Key]
    public int Id { get; set; }

    public int SongId { get; set; }
    public Song Song { get; set; } = null!;

    [Required]
    [MaxLength(10)]
    public string Key { get; set; } = string.Empty; // e.g., "C", "G", "D", "Em"

    [Required]
    public string Content { get; set; } = string.Empty; // The chord chart with chords and lyrics

    [MaxLength(50)]
    public string? Capo { get; set; } // e.g., "Capo 3"

    [MaxLength(20)]
    public string? TimeSignature { get; set; } // e.g., "4/4", "3/4", "6/8"

    public ChordChartFormat Format { get; set; } = ChordChartFormat.ChordPro;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Who created this version
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}

public enum ChordChartFormat
{
    ChordPro,    // Standard ChordPro format [C]Amazing [G]Grace
    PlainText,   // Simple text with chords above lyrics
    OnSong       // OnSong app format
}