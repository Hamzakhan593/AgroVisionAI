using AgroVisionAI.Data;
using AgroVisionAI.Models;
using AgroVisionAI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroVisionAI.Controllers
{
    [Authorize]
    public sealed class AIAnalyticsController : Controller
    {
        private const double LowConfidenceThreshold = 0.75;

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAgroVisionApiClient _aiClient;
        private readonly ILogger<AIAnalyticsController> _logger;

        public AIAnalyticsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IAgroVisionApiClient aiClient,
            ILogger<AIAnalyticsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _aiClient = aiClient;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Admins see platform-wide quality metrics. Every other authenticated user
            // gets the same dashboard safely scoped to only their own analyses/feedback.
            var isAdmin = User.IsInRole("Admin");

            IQueryable<Detection> detections = _context.Detections.AsNoTracking();
            IQueryable<PredictionFeedback> feedback = _context.PredictionFeedbacks.AsNoTracking();

            if (!isAdmin)
            {
                detections = detections.Where(item => item.UserId == user.Id);
                feedback = feedback.Where(item => item.UserId == user.Id);
            }

            var now = DateTime.UtcNow;
            var last7Days = now.AddDays(-7);
            var trendStart = now.Date.AddDays(-13);

            var totalAnalyses = await detections.CountAsync(cancellationToken);
            var analysesLast7Days = await detections
                .CountAsync(item => item.CreatedAt >= last7Days, cancellationToken);
            var lowConfidenceCount = await detections
                .CountAsync(item => item.Confidence.HasValue && item.Confidence.Value < LowConfidenceThreshold, cancellationToken);

            var averageDiseaseConfidence = await AverageOrNullAsync(
                detections.Where(item => item.Confidence.HasValue).Select(item => item.Confidence!.Value),
                cancellationToken);
            var averageCropConfidence = await AverageOrNullAsync(
                detections.Where(item => item.CropConfidence.HasValue).Select(item => item.CropConfidence!.Value),
                cancellationToken);
            var averageProcessingTime = await AverageOrNullAsync(
                detections.Where(item => item.ProcessingTimeMs.HasValue).Select(item => item.ProcessingTimeMs!.Value),
                cancellationToken);
            var averageMargin = await AverageOrNullAsync(
                detections.Where(item => item.ConfidenceMargin.HasValue).Select(item => item.ConfidenceMargin!.Value),
                cancellationToken);

            var feedbackTotal = await feedback.CountAsync(cancellationToken);
            var feedbackCorrect = await feedback.CountAsync(item => item.Verdict == FeedbackVerdict.Correct, cancellationToken);
            var feedbackIncorrect = await feedback.CountAsync(item => item.Verdict == FeedbackVerdict.Incorrect, cancellationToken);
            var feedbackNotSure = await feedback.CountAsync(item => item.Verdict == FeedbackVerdict.NotSure, cancellationToken);
            var feedbackPending = await feedback.CountAsync(
                item => item.VerificationStatus == FeedbackVerificationStatus.Pending,
                cancellationToken);

            var confirmedDenominator = feedbackCorrect + feedbackIncorrect;
            var confirmedAccuracy = confirmedDenominator == 0
                ? (double?)null
                : feedbackCorrect / (double)confirmedDenominator;

            var cropDistribution = await detections
                .GroupBy(item => item.Crop)
                .Select(group => new AnalyticsPoint
                {
                    Label = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ToListAsync(cancellationToken);

            var diseaseDistribution = await detections
                .Where(item => item.Disease != null)
                .GroupBy(item => item.Disease!.Name)
                .Select(group => new AnalyticsPoint
                {
                    Label = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .Take(8)
                .ToListAsync(cancellationToken);

            var rawDaily = await detections
                .Where(item => item.CreatedAt >= trendStart)
                .GroupBy(item => item.CreatedAt.Date)
                .Select(group => new
                {
                    Date = group.Key,
                    Count = group.Count()
                })
                .ToListAsync(cancellationToken);

            var dailyLookup = rawDaily.ToDictionary(item => item.Date, item => item.Count);
            var dailyAnalyses = Enumerable.Range(0, 14)
                .Select(offset => trendStart.AddDays(offset))
                .Select(date => new AnalyticsPoint
                {
                    Label = date.ToString("dd MMM"),
                    Count = dailyLookup.TryGetValue(date, out var count) ? count : 0
                })
                .ToList();

            var confidenceValues = await detections
                .Where(item => item.Confidence.HasValue)
                .Select(item => item.Confidence!.Value)
                .ToListAsync(cancellationToken);

            var confidenceBands = new List<AnalyticsPoint>
            {
                new() { Label = "90-100%", Count = confidenceValues.Count(value => value >= 0.90) },
                new() { Label = "80-89%", Count = confidenceValues.Count(value => value >= 0.80 && value < 0.90) },
                new() { Label = "70-79%", Count = confidenceValues.Count(value => value >= 0.70 && value < 0.80) },
                new() { Label = "Below 70%", Count = confidenceValues.Count(value => value < 0.70) }
            };

            var modelUsage = await detections
                .Where(item => item.DiseaseModelName != null)
                .GroupBy(item => new
                {
                    item.Crop,
                    item.DiseaseModelName,
                    item.DiseaseModelVersion
                })
                .Select(group => new ModelUsageRow
                {
                    Crop = group.Key.Crop,
                    ModelName = group.Key.DiseaseModelName ?? "Unknown model",
                    Version = group.Key.DiseaseModelVersion ?? "Unversioned",
                    Predictions = group.Count(),
                    AverageConfidence = group.Average(item => item.Confidence),
                    AverageLatencyMs = group.Average(item => item.ProcessingTimeMs)
                })
                .OrderByDescending(item => item.Predictions)
                .ToListAsync(cancellationToken);

            var recentLowConfidence = await detections
                .Where(item => item.Confidence.HasValue && item.Confidence.Value < LowConfidenceThreshold)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new LowConfidenceRow
                {
                    DetectionId = item.Id,
                    Crop = item.Crop,
                    Disease = item.Disease != null ? item.Disease.Name : "Unknown",
                    Confidence = item.Confidence!.Value,
                    ConfidenceMargin = item.ConfidenceMargin,
                    ModelVersion = item.DiseaseModelVersion ?? "Unversioned",
                    CreatedAt = item.CreatedAt
                })
                .Take(8)
                .ToListAsync(cancellationToken);

            var recentFeedback = await feedback
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => new RecentFeedbackRow
                {
                    DetectionId = item.DetectionId,
                    Crop = item.Detection != null ? item.Detection.Crop : "Unknown",
                    Disease = item.Detection != null && item.Detection.Disease != null
                        ? item.Detection.Disease.Name
                        : "Unknown",
                    Verdict = item.Verdict,
                    VerificationStatus = item.VerificationStatus,
                    UpdatedAt = item.UpdatedAt
                })
                .Take(8)
                .ToListAsync(cancellationToken);

            var model = new AIAnalyticsViewModel
            {
                IsAdminScope = isAdmin,
                TotalUsers = isAdmin ? await _userManager.Users.CountAsync(cancellationToken) : null,
                TotalAnalyses = totalAnalyses,
                AnalysesLast7Days = analysesLast7Days,
                LowConfidenceCount = lowConfidenceCount,
                AverageDiseaseConfidence = averageDiseaseConfidence,
                AverageCropConfidence = averageCropConfidence,
                AverageProcessingTimeMs = averageProcessingTime,
                AverageConfidenceMargin = averageMargin,
                FeedbackTotal = feedbackTotal,
                FeedbackCorrect = feedbackCorrect,
                FeedbackIncorrect = feedbackIncorrect,
                FeedbackNotSure = feedbackNotSure,
                FeedbackPendingReview = feedbackPending,
                UserConfirmedAccuracy = confirmedAccuracy,
                CropDistribution = cropDistribution,
                DiseaseDistribution = diseaseDistribution,
                DailyAnalyses = dailyAnalyses,
                ConfidenceBands = confidenceBands,
                ModelUsage = modelUsage,
                RecentLowConfidence = recentLowConfidence,
                RecentFeedback = recentFeedback
            };

            try
            {
                var health = await _aiClient.GetHealthAsync(cancellationToken);
                model.AiHealthReachable = true;
                model.AiHealthStatus = string.IsNullOrWhiteSpace(health.Status) ? "unknown" : health.Status;
                model.AiModels = health.Models ?? new();
            }
            catch (AgroVisionApiException exc)
            {
                _logger.LogWarning(exc, "AI health check failed while opening analytics dashboard.");
                model.AiHealthReachable = false;
                model.AiHealthStatus = "unavailable";
                model.AiHealthError = exc.Message;
            }

            return View(model);
        }

        private static async Task<double?> AverageOrNullAsync(
            IQueryable<double> query,
            CancellationToken cancellationToken)
        {
            return await query.Select(value => (double?)value).AverageAsync(cancellationToken);
        }
    }
}
