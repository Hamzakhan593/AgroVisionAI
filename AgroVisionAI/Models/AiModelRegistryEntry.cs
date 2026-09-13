using System.ComponentModel.DataAnnotations;

namespace AgroVisionAI.Models
{
    public enum AiModelLifecycleStatus
    {
        Candidate = 0,
        Active = 1,
        Archived = 2
    }

    public sealed class AiModelRegistryEntry
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Crop { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Filename { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Version { get; set; } = "unversioned";

        [MaxLength(120)]
        public string? Architecture { get; set; }

        public double? Accuracy { get; set; }
        public double? MacroF1 { get; set; }
        public double? ExternalAccuracy { get; set; }

        public AiModelLifecycleStatus Status { get; set; } = AiModelLifecycleStatus.Candidate;

        public bool IsAvailable { get; set; }
        public bool IsLoaded { get; set; }
        public bool SelectionLockedByEnvironment { get; set; }

        [MaxLength(50)]
        public string? Preprocessing { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        [MaxLength(450)]
        public string? LastActivatedByUserId { get; set; }

        public DateTime? ActivatedAtUtc { get; set; }
        public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
