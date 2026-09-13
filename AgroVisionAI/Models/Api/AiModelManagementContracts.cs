using System.Text.Json.Serialization;

namespace AgroVisionAI.Models.Api
{
    public sealed class AiManagedModelStatus
    {
        [JsonPropertyName("crop")]
        public string Crop { get; set; } = string.Empty;

        [JsonPropertyName("filename")]
        public string Filename { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("available")]
        public bool Available { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("loaded")]
        public bool Loaded { get; set; }

        [JsonPropertyName("preprocessing")]
        public string? Preprocessing { get; set; }

        [JsonPropertyName("classes")]
        public List<string> Classes { get; set; } = new();

        [JsonPropertyName("selection_locked_by_environment")]
        public bool SelectionLockedByEnvironment { get; set; }
    }

    public sealed class AiActivateModelRequest
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = string.Empty;
    }
}
