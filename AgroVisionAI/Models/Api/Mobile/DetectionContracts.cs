using AgroVisionAI.Models;
using System.Text.Json.Serialization;

namespace AgroVisionAI.Models.Api.Mobile
{
    public class DetectionListItemResponse
    {
        public int Id { get; set; }
        public string Crop { get; set; } = string.Empty;
        public string Disease { get; set; } = string.Empty;
        public double? Confidence { get; set; }
        public double? CropConfidence { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string? ExplanationUrl { get; set; }
        public string ReportUrl { get; set; } = string.Empty;
    }

    public sealed class DetectionDetailResponse : DetectionListItemResponse
    {
        public string? RequestId { get; set; }
        public string? PredictedClass { get; set; }
        public string? CropModelName { get; set; }
        public string? CropModelVersion { get; set; }
        public string? DiseaseModelName { get; set; }
        public string? DiseaseModelVersion { get; set; }
        public string? SecondPredictionName { get; set; }
        public double? SecondConfidence { get; set; }
        public double? ConfidenceMargin { get; set; }
        public double? ProcessingTimeMs { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Symptoms { get; set; } = string.Empty;
        public string Treatment { get; set; } = string.Empty;
        public string Prevention { get; set; } = string.Empty;
        public PredictionFeedbackResponse? Feedback { get; set; }
        public IReadOnlyList<string> CorrectionOptions { get; set; } = Array.Empty<string>();
    }

    public sealed class PredictionFeedbackRequest
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FeedbackVerdict Verdict { get; set; }

        public string? SuggestedDisease { get; set; }
        public string? Comment { get; set; }
    }

    public sealed class PredictionFeedbackResponse
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FeedbackVerdict Verdict { get; set; }

        public string? SuggestedDisease { get; set; }
        public string? Comment { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FeedbackVerificationStatus VerificationStatus { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }
}
