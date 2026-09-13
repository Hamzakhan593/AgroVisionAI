using System.Text.Json.Serialization;

namespace AgroVisionAI.Models.Api
{
    public sealed class TopPredictionResponse
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("confidence_percent")]
        public double ConfidencePercent { get; set; }
    }

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

        [JsonPropertyName("crop_model_version")]
        public string CropModelVersion { get; set; } = string.Empty;

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

        [JsonPropertyName("top_predictions")]
        public List<TopPredictionResponse> TopPredictions { get; set; } = new();

        [JsonPropertyName("model_name")]
        public string ModelName { get; set; } = string.Empty;

        [JsonPropertyName("model_version")]
        public string ModelVersion { get; set; } = string.Empty;

        [JsonPropertyName("processing_time_ms")]
        public double ProcessingTimeMs { get; set; }

        [JsonPropertyName("explanation_available")]
        public bool ExplanationAvailable { get; set; }

        [JsonPropertyName("explanation_method")]
        public string? ExplanationMethod { get; set; }

        [JsonPropertyName("explanation_layer")]
        public string? ExplanationLayer { get; set; }

        [JsonPropertyName("explanation_image_base64")]
        public string? ExplanationImageBase64 { get; set; }

        [JsonPropertyName("explanation_image_media_type")]
        public string? ExplanationImageMediaType { get; set; }

        [JsonPropertyName("disclaimer")]
        public string Disclaimer { get; set; } = string.Empty;
    }
}
