using AgroVisionAI.TreatmentPk;

namespace AgroVisionAI.Models.Reports
{
    public sealed class DiagnosticReportData
    {
        public required Detection Detection { get; init; }
        public required ApplicationUser User { get; init; }
        public PredictionFeedback? Feedback { get; init; }
        public required TreatmentResult Treatment { get; init; }
        public byte[]? UploadedImage { get; init; }
        public byte[]? ExplanationImage { get; init; }
        public int? DaysToHarvest { get; init; }
        public bool? WhiteflyTreatmentNeeded { get; init; }
        public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    }
}
