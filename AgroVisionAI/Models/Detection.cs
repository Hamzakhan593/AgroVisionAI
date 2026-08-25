using System.ComponentModel.DataAnnotations;

namespace AgroVisionAI.Models
{
    public class Detection
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Crop { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string ImagePath { get; set; } = string.Empty;

        public int? DiseaseId { get; set; }

        public double? Confidence { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser? User { get; set; }

        public Disease? Disease { get; set; }
    }
}