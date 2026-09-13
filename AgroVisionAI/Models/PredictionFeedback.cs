using System.ComponentModel.DataAnnotations;

namespace AgroVisionAI.Models
{
    public enum FeedbackVerdict
    {
        Correct = 1,
        Incorrect = 2,
        NotSure = 3
    }

    public enum FeedbackVerificationStatus
    {
        Pending = 0,
        Verified = 1,
        Rejected = 2
    }

    /// <summary>
    /// Human feedback attached to one saved AI detection. A detection can have only
    /// one current feedback record; resubmitting updates the record and sends it
    /// back to Pending so future expert/admin review can audit the latest correction.
    /// </summary>
    public class PredictionFeedback
    {
        public int Id { get; set; }

        [Required]
        public int DetectionId { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public FeedbackVerdict Verdict { get; set; }

        [MaxLength(150)]
        public string? SuggestedDisease { get; set; }

        [MaxLength(500)]
        public string? Comment { get; set; }

        public FeedbackVerificationStatus VerificationStatus { get; set; } = FeedbackVerificationStatus.Pending;

        // Reserved for the expert/admin verification stage. Task 2 only collects
        // user feedback; a later admin workflow can populate these audit fields.
        [MaxLength(150)]
        public string? VerifiedDisease { get; set; }

        [MaxLength(500)]
        public string? ReviewerNote { get; set; }

        public DateTime? VerifiedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Detection? Detection { get; set; }
    }
}
