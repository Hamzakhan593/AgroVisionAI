using System.Text.Json.Serialization;

namespace AgroVisionAI.Models.Api
{
    public sealed class CropPredictionResponse
    {
        [JsonPropertyName("request_id")]
        public string RequestId { get; set; } = string.Empty;

        [JsonPropertyName("crop")]
        public string Crop { get; set; } = string.Empty;

        [JsonPropertyName("crop_confidence")]
        public double? CropConfidence { get; set; }

        [JsonPropertyName("crop_model_name")]
        public string CropModelName { get; set; } = string.Empty;

        [JsonPropertyName("predicted_class")]
        public string PredictedClass { get; set; } = string.Empty;

        [JsonPropertyName("disease")]
        public string Disease { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("confidence_percent")]
        public double ConfidencePercent { get; set; }

        [JsonPropertyName("is_healthy")]
        public bool IsHealthy { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("symptoms")]
        public string Symptoms { get; set; } = string.Empty;

        [JsonPropertyName("treatment")]
        public string Treatment { get; set; } = string.Empty;

        [JsonPropertyName("prevention")]
        public string Prevention { get; set; } = string.Empty;

        [JsonPropertyName("model_name")]
        public string ModelName { get; set; } = string.Empty;
    }
}
