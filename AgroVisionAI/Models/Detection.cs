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

        // Disease-model confidence (0..1). Kept for backward compatibility with
        // existing treatment/history code while richer inference metadata is stored below.
        public double? Confidence { get; set; }

        [MaxLength(64)]
        public string? RequestId { get; set; }

        public double? CropConfidence { get; set; }

        [MaxLength(200)]
        public string? CropModelName { get; set; }

        [MaxLength(50)]
        public string? CropModelVersion { get; set; }

        [MaxLength(100)]
        public string? PredictedClass { get; set; }

        [MaxLength(200)]
        public string? DiseaseModelName { get; set; }

        [MaxLength(50)]
        public string? DiseaseModelVersion { get; set; }

        [MaxLength(100)]
        public string? SecondPredictedClass { get; set; }

        [MaxLength(150)]
        public string? SecondPredictionName { get; set; }

        public double? SecondConfidence { get; set; }

        // Top-1 disease confidence minus Top-2 disease confidence (0..1).
        public double? ConfidenceMargin { get; set; }

        public double? ProcessingTimeMs { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser? User { get; set; }

        public Disease? Disease { get; set; }
    }
}
