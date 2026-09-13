using System.ComponentModel.DataAnnotations;

namespace AgroVisionAI.Models.ModelRegistry
{
    public sealed class ModelRegistryIndexViewModel
    {
        public IReadOnlyList<AiModelRegistryEntry> Models { get; init; } = Array.Empty<AiModelRegistryEntry>();
        public bool AiServiceReachable { get; init; }
        public string? ServiceMessage { get; init; }
    }

    public sealed class UpdateModelMetadataViewModel
    {
        [Required]
        public int Id { get; set; }

        [MaxLength(120)]
        public string? Architecture { get; set; }

        [Range(0, 1)]
        public double? Accuracy { get; set; }

        [Range(0, 1)]
        public double? MacroF1 { get; set; }

        [Range(0, 1)]
        public double? ExternalAccuracy { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
