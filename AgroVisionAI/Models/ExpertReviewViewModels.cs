using AgroVisionAI.Models;

namespace AgroVisionAI.Models
{
    public sealed class ExpertReviewQueueViewModel
    {
        public string StatusFilter { get; set; } = "pending";
        public int PendingCount { get; set; }
        public int VerifiedCount { get; set; }
        public int RejectedCount { get; set; }
        public IReadOnlyList<ExpertReviewQueueItem> Items { get; set; } = Array.Empty<ExpertReviewQueueItem>();
    }

    public sealed class ExpertReviewQueueItem
    {
        public int FeedbackId { get; set; }
        public int DetectionId { get; set; }
        public string Crop { get; set; } = string.Empty;
        public string AiDisease { get; set; } = string.Empty;
        public double? Confidence { get; set; }
        public FeedbackVerdict Verdict { get; set; }
        public string? SuggestedDisease { get; set; }
        public string? Comment { get; set; }
        public FeedbackVerificationStatus VerificationStatus { get; set; }
        public string UserDisplayName { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ExpertReviewDetailsViewModel
    {
        public int FeedbackId { get; set; }
        public int DetectionId { get; set; }
        public string Crop { get; set; } = string.Empty;
        public string AiDisease { get; set; } = string.Empty;
        public double? Confidence { get; set; }
        public double? CropConfidence { get; set; }
        public string? DiseaseModelName { get; set; }
        public string? DiseaseModelVersion { get; set; }
        public string? RequestId { get; set; }
        public FeedbackVerdict Verdict { get; set; }
        public string? SuggestedDisease { get; set; }
        public string? Comment { get; set; }
        public FeedbackVerificationStatus VerificationStatus { get; set; }
        public string? VerifiedDisease { get; set; }
        public string? ReviewerNote { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string UserDisplayName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public DateTime DetectionCreatedAt { get; set; }
        public DateTime FeedbackUpdatedAt { get; set; }
        public IReadOnlyList<string> DiseaseOptions { get; set; } = Array.Empty<string>();
        public bool HasExplanationImage { get; set; }
    }
}
