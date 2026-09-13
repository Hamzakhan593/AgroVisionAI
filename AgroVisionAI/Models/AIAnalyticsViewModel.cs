using AgroVisionAI.Models.Api;

namespace AgroVisionAI.Models
{
    public sealed class AIAnalyticsViewModel
    {
        public bool IsAdminScope { get; set; }
        public string ScopeLabel => IsAdminScope ? "Platform analytics" : "Personal analytics";

        public int? TotalUsers { get; set; }
        public int TotalAnalyses { get; set; }
        public int AnalysesLast7Days { get; set; }
        public int LowConfidenceCount { get; set; }
        public double? AverageDiseaseConfidence { get; set; }
        public double? AverageCropConfidence { get; set; }
        public double? AverageProcessingTimeMs { get; set; }
        public double? AverageConfidenceMargin { get; set; }

        public int FeedbackTotal { get; set; }
        public int FeedbackCorrect { get; set; }
        public int FeedbackIncorrect { get; set; }
        public int FeedbackNotSure { get; set; }
        public int FeedbackPendingReview { get; set; }
        public double? UserConfirmedAccuracy { get; set; }

        public bool AiHealthReachable { get; set; }
        public string AiHealthStatus { get; set; } = "unavailable";
        public string? AiHealthError { get; set; }
        public List<AiModelStatus> AiModels { get; set; } = new();

        public List<AnalyticsPoint> DailyAnalyses { get; set; } = new();
        public List<AnalyticsPoint> CropDistribution { get; set; } = new();
        public List<AnalyticsPoint> DiseaseDistribution { get; set; } = new();
        public List<AnalyticsPoint> ConfidenceBands { get; set; } = new();
        public List<ModelUsageRow> ModelUsage { get; set; } = new();
        public List<LowConfidenceRow> RecentLowConfidence { get; set; } = new();
        public List<RecentFeedbackRow> RecentFeedback { get; set; } = new();
    }

    public sealed class AnalyticsPoint
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public sealed class ModelUsageRow
    {
        public string Crop { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public int Predictions { get; set; }
        public double? AverageConfidence { get; set; }
        public double? AverageLatencyMs { get; set; }
    }

    public sealed class LowConfidenceRow
    {
        public int DetectionId { get; set; }
        public string Crop { get; set; } = string.Empty;
        public string Disease { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public double? ConfidenceMargin { get; set; }
        public string ModelVersion { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public sealed class RecentFeedbackRow
    {
        public int DetectionId { get; set; }
        public string Crop { get; set; } = string.Empty;
        public string Disease { get; set; } = string.Empty;
        public FeedbackVerdict Verdict { get; set; }
        public FeedbackVerificationStatus VerificationStatus { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
