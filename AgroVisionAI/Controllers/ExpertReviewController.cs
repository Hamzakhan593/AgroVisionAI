using System.Globalization;
using System.IO.Compression;
using System.Text;
using AgroVisionAI.Data;
using AgroVisionAI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroVisionAI.Controllers
{
    [Authorize(Roles = "Admin")]
    public sealed class ExpertReviewController : Controller
    {
        private static readonly IReadOnlyDictionary<string, string[]> DiseaseOptions =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cotton"] = new[] { "Bacterial Blight", "Cotton Leaf Curl Virus", "Healthy Cotton" },
                ["Wheat"] = new[] { "Wheat Brown Rust", "Wheat Yellow Rust", "Healthy Wheat" },
                ["Rice"] = new[] { "Rice Bacterial Leaf Blight", "Rice Brown Spot", "Rice Leaf Blast", "Rice Tungro", "Healthy Rice" }
            };

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ExpertReviewController> _logger;

        public ExpertReviewController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            ILogger<ExpertReviewController> logger)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string status = "pending", CancellationToken cancellationToken = default)
        {
            status = NormalizeStatus(status);

            var pendingCount = await _context.PredictionFeedbacks
                .AsNoTracking()
                .CountAsync(item => item.VerificationStatus == FeedbackVerificationStatus.Pending, cancellationToken);
            var verifiedCount = await _context.PredictionFeedbacks
                .AsNoTracking()
                .CountAsync(item => item.VerificationStatus == FeedbackVerificationStatus.Verified, cancellationToken);
            var rejectedCount = await _context.PredictionFeedbacks
                .AsNoTracking()
                .CountAsync(item => item.VerificationStatus == FeedbackVerificationStatus.Rejected, cancellationToken);

            var query = _context.PredictionFeedbacks
                .AsNoTracking()
                .Include(item => item.Detection)
                    .ThenInclude(item => item!.Disease)
                .AsQueryable();

            query = status switch
            {
                "verified" => query.Where(item => item.VerificationStatus == FeedbackVerificationStatus.Verified),
                "rejected" => query.Where(item => item.VerificationStatus == FeedbackVerificationStatus.Rejected),
                "all" => query,
                _ => query.Where(item => item.VerificationStatus == FeedbackVerificationStatus.Pending)
            };

            var feedbackRows = await query
                .OrderByDescending(item => item.UpdatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);

            var userIds = feedbackRows
                .Select(item => item.UserId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToArray();

            var users = await _context.Users
                .AsNoTracking()
                .Where(user => userIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, cancellationToken);

            var items = feedbackRows
                .Where(item => item.Detection != null)
                .Select(item =>
                {
                    users.TryGetValue(item.UserId, out var user);
                    return new ExpertReviewQueueItem
                    {
                        FeedbackId = item.Id,
                        DetectionId = item.DetectionId,
                        Crop = item.Detection!.Crop,
                        AiDisease = GetAiDisease(item.Detection),
                        Confidence = item.Detection.Confidence,
                        Verdict = item.Verdict,
                        SuggestedDisease = item.SuggestedDisease,
                        Comment = item.Comment,
                        VerificationStatus = item.VerificationStatus,
                        UserDisplayName = GetDisplayName(user),
                        UpdatedAt = item.UpdatedAt
                    };
                })
                .ToArray();

            return View(new ExpertReviewQueueViewModel
            {
                StatusFilter = status,
                PendingCount = pendingCount,
                VerifiedCount = verifiedCount,
                RejectedCount = rejectedCount,
                Items = items
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
        {
            var feedback = await _context.PredictionFeedbacks
                .AsNoTracking()
                .Include(item => item.Detection)
                    .ThenInclude(item => item!.Disease)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (feedback?.Detection == null)
            {
                return NotFound();
            }

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == feedback.UserId, cancellationToken);

            var cropOptions = DiseaseOptions.TryGetValue(feedback.Detection.Crop, out var options)
                ? options
                : Array.Empty<string>();

            var model = new ExpertReviewDetailsViewModel
            {
                FeedbackId = feedback.Id,
                DetectionId = feedback.DetectionId,
                Crop = feedback.Detection.Crop,
                AiDisease = GetAiDisease(feedback.Detection),
                Confidence = feedback.Detection.Confidence,
                CropConfidence = feedback.Detection.CropConfidence,
                DiseaseModelName = feedback.Detection.DiseaseModelName,
                DiseaseModelVersion = feedback.Detection.DiseaseModelVersion,
                RequestId = feedback.Detection.RequestId,
                Verdict = feedback.Verdict,
                SuggestedDisease = feedback.SuggestedDisease,
                Comment = feedback.Comment,
                VerificationStatus = feedback.VerificationStatus,
                VerifiedDisease = feedback.VerifiedDisease,
                ReviewerNote = feedback.ReviewerNote,
                VerifiedAt = feedback.VerifiedAt,
                UserDisplayName = GetDisplayName(user),
                UserEmail = user?.Email ?? string.Empty,
                DetectionCreatedAt = feedback.Detection.CreatedAt,
                FeedbackUpdatedAt = feedback.UpdatedAt,
                DiseaseOptions = cropOptions,
                HasExplanationImage = System.IO.File.Exists(GetGradCamPath(feedback.DetectionId))
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(
            int feedbackId,
            string decision,
            string? verifiedDisease,
            string? reviewerNote,
            CancellationToken cancellationToken = default)
        {
            var feedback = await _context.PredictionFeedbacks
                .Include(item => item.Detection)
                    .ThenInclude(item => item!.Disease)
                .FirstOrDefaultAsync(item => item.Id == feedbackId, cancellationToken);

            if (feedback?.Detection == null)
            {
                return NotFound();
            }

            reviewerNote = string.IsNullOrWhiteSpace(reviewerNote) ? null : reviewerNote.Trim();
            if (reviewerNote?.Length > 500)
            {
                TempData["ExpertReviewError"] = "Reviewer note must be 500 characters or fewer.";
                return RedirectToAction(nameof(Details), new { id = feedbackId });
            }

            var reviewer = await _userManager.GetUserAsync(User);
            var reviewerLabel = reviewer?.Email ?? reviewer?.FullName ?? "Admin reviewer";
            var now = DateTime.UtcNow;

            switch ((decision ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "approve-correction":
                {
                    if (feedback.Verdict != FeedbackVerdict.Incorrect || string.IsNullOrWhiteSpace(feedback.SuggestedDisease))
                    {
                        TempData["ExpertReviewError"] = "There is no user correction available to approve.";
                        return RedirectToAction(nameof(Details), new { id = feedbackId });
                    }

                    var canonicalDisease = CanonicalDisease(feedback.Detection.Crop, feedback.SuggestedDisease);
                    if (canonicalDisease == null)
                    {
                        TempData["ExpertReviewError"] = "The suggested label is not valid for this crop.";
                        return RedirectToAction(nameof(Details), new { id = feedbackId });
                    }

                    feedback.VerificationStatus = FeedbackVerificationStatus.Verified;
                    feedback.VerifiedDisease = canonicalDisease;
                    feedback.ReviewerNote = ComposeReviewerNote(reviewerLabel, reviewerNote, "User correction verified.");
                    feedback.VerifiedAt = now;
                    break;
                }

                case "verify-ai":
                {
                    var aiDisease = GetAiDisease(feedback.Detection);
                    var canonicalDisease = CanonicalDisease(feedback.Detection.Crop, aiDisease) ?? aiDisease;
                    if (string.IsNullOrWhiteSpace(canonicalDisease))
                    {
                        TempData["ExpertReviewError"] = "The AI label is missing and cannot be verified.";
                        return RedirectToAction(nameof(Details), new { id = feedbackId });
                    }

                    feedback.VerificationStatus = FeedbackVerificationStatus.Verified;
                    feedback.VerifiedDisease = canonicalDisease;
                    feedback.ReviewerNote = ComposeReviewerNote(reviewerLabel, reviewerNote, "AI prediction verified.");
                    feedback.VerifiedAt = now;
                    break;
                }

                case "verify-selected":
                {
                    var canonicalDisease = CanonicalDisease(feedback.Detection.Crop, verifiedDisease);
                    if (canonicalDisease == null)
                    {
                        TempData["ExpertReviewError"] = "Select a valid verified condition for this crop.";
                        return RedirectToAction(nameof(Details), new { id = feedbackId });
                    }

                    feedback.VerificationStatus = FeedbackVerificationStatus.Verified;
                    feedback.VerifiedDisease = canonicalDisease;
                    feedback.ReviewerNote = ComposeReviewerNote(reviewerLabel, reviewerNote, "Reviewer selected the final label.");
                    feedback.VerifiedAt = now;
                    break;
                }

                case "reject":
                    feedback.VerificationStatus = FeedbackVerificationStatus.Rejected;
                    feedback.VerifiedDisease = null;
                    feedback.ReviewerNote = ComposeReviewerNote(reviewerLabel, reviewerNote, "Feedback rejected as a training label.");
                    feedback.VerifiedAt = now;
                    break;

                default:
                    TempData["ExpertReviewError"] = "Choose a valid review action.";
                    return RedirectToAction(nameof(Details), new { id = feedbackId });
            }

            await _context.SaveChangesAsync(cancellationToken);
            TempData["ExpertReviewSuccess"] = "Review decision saved.";
            return RedirectToAction(nameof(Details), new { id = feedbackId });
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Image(int id, CancellationToken cancellationToken = default)
        {
            var detection = await _context.Detections
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (detection == null)
            {
                return NotFound();
            }

            var imagePath = GetDetectionImagePath(detection);
            if (!System.IO.File.Exists(imagePath))
            {
                return NotFound();
            }

            return PhysicalFile(imagePath, GetImageContentType(imagePath));
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Explanation(int id, CancellationToken cancellationToken = default)
        {
            var exists = await _context.Detections
                .AsNoTracking()
                .AnyAsync(item => item.Id == id, cancellationToken);
            if (!exists)
            {
                return NotFound();
            }

            var path = GetGradCamPath(id);
            if (!System.IO.File.Exists(path))
            {
                return NotFound();
            }

            return PhysicalFile(path, "image/jpeg");
        }

        [HttpGet]
        public async Task<IActionResult> ExportVerified(CancellationToken cancellationToken = default)
        {
            var feedbackRows = await _context.PredictionFeedbacks
                .AsNoTracking()
                .Include(item => item.Detection)
                    .ThenInclude(item => item!.Disease)
                .Where(item =>
                    item.VerificationStatus == FeedbackVerificationStatus.Verified &&
                    item.VerifiedDisease != null)
                .OrderBy(item => item.Id)
                .ToListAsync(cancellationToken);

            if (feedbackRows.Count == 0)
            {
                TempData["ExpertReviewError"] = "There are no verified samples to export yet.";
                return RedirectToAction(nameof(Index), new { status = "verified" });
            }

            await using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = new StringBuilder();
                manifest.AppendLine("feedback_id,detection_id,crop,ai_prediction,verified_label,disease_confidence,crop_confidence,model_name,model_version,request_id,created_at_utc,image_path");

                var exportedCount = 0;
                foreach (var feedback in feedbackRows)
                {
                    if (feedback.Detection == null || string.IsNullOrWhiteSpace(feedback.VerifiedDisease))
                    {
                        continue;
                    }

                    var sourcePath = GetDetectionImagePath(feedback.Detection);
                    if (!System.IO.File.Exists(sourcePath))
                    {
                        continue;
                    }

                    var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                    if (extension is not ".jpg" and not ".jpeg" and not ".png")
                    {
                        continue;
                    }

                    var cropFolder = SafePathSegment(feedback.Detection.Crop);
                    var labelFolder = SafePathSegment(feedback.VerifiedDisease);
                    var archiveImagePath = $"images/{cropFolder}/{labelFolder}/{feedback.DetectionId}{extension}";

                    var imageEntry = archive.CreateEntry(archiveImagePath, CompressionLevel.Fastest);
                    await using (var target = imageEntry.Open())
                    await using (var source = System.IO.File.OpenRead(sourcePath))
                    {
                        await source.CopyToAsync(target, cancellationToken);
                    }

                    manifest.AppendLine(string.Join(",",
                        Csv(feedback.Id.ToString(CultureInfo.InvariantCulture)),
                        Csv(feedback.DetectionId.ToString(CultureInfo.InvariantCulture)),
                        Csv(feedback.Detection.Crop),
                        Csv(GetAiDisease(feedback.Detection)),
                        Csv(feedback.VerifiedDisease),
                        Csv(FormatNullable(feedback.Detection.Confidence)),
                        Csv(FormatNullable(feedback.Detection.CropConfidence)),
                        Csv(feedback.Detection.DiseaseModelName),
                        Csv(feedback.Detection.DiseaseModelVersion),
                        Csv(feedback.Detection.RequestId),
                        Csv(feedback.Detection.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                        Csv(archiveImagePath)));
                    exportedCount++;
                }

                if (exportedCount == 0)
                {
                    TempData["ExpertReviewError"] = "Verified feedback exists, but no source images were available to export.";
                    return RedirectToAction(nameof(Index), new { status = "verified" });
                }

                var manifestEntry = archive.CreateEntry("manifest.csv", CompressionLevel.Fastest);
                await using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                {
                    await writer.WriteAsync(manifest.ToString());
                }

                var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Fastest);
                await using (var writer = new StreamWriter(readmeEntry.Open(), new UTF8Encoding(false)))
                {
                    await writer.WriteAsync(
                        "AgroVisionAI verified feedback export\n\n" +
                        "This package contains only admin-reviewed samples. It is a curated retraining candidate set, not an automatically trusted training set. " +
                        "Re-audit image quality, duplicates, leakage and class balance before retraining any model.\n");
                }
            }

            output.Position = 0;
            var fileName = $"AgroVisionAI_Verified_Feedback_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
            return File(output.ToArray(), "application/zip", fileName);
        }

        private string GetDetectionImagePath(Detection detection)
        {
            return Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "CropImages",
                Path.GetFileName(detection.ImagePath));
        }

        private string GetGradCamPath(int detectionId)
        {
            return Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "Explainability",
                $"{detectionId}.jpg");
        }

        private static string GetImageContentType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() == ".png"
                ? "image/png"
                : "image/jpeg";
        }

        private static string GetAiDisease(Detection detection)
        {
            return detection.Disease?.Name
                ?? detection.PredictedClass
                ?? "Unknown";
        }

        private static string NormalizeStatus(string? status)
        {
            return (status ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "verified" => "verified",
                "rejected" => "rejected",
                "all" => "all",
                _ => "pending"
            };
        }

        private static string? CanonicalDisease(string crop, string? disease)
        {
            if (string.IsNullOrWhiteSpace(disease) || !DiseaseOptions.TryGetValue(crop, out var options))
            {
                return null;
            }

            return options.FirstOrDefault(item =>
                string.Equals(item, disease.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDisplayName(ApplicationUser? user)
        {
            if (user == null)
            {
                return "Unknown user";
            }

            if (!string.IsNullOrWhiteSpace(user.FullName))
            {
                return user.FullName;
            }

            return user.Email ?? user.UserName ?? "User";
        }

        private static string ComposeReviewerNote(string reviewer, string? note, string decisionSummary)
        {
            var text = string.IsNullOrWhiteSpace(note)
                ? decisionSummary
                : $"{decisionSummary} {note.Trim()}";
            return $"Reviewed by {reviewer}. {text}";
        }

        private static string SafePathSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value
                .Trim()
                .Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch)
                .ToArray();
            return new string(chars).Replace(' ', '_');
        }

        private static string Csv(string? value)
        {
            var normalized = value ?? string.Empty;
            return $"\"{normalized.Replace("\"", "\"\"")}\"";
        }

        private static string FormatNullable(double? value)
        {
            return value?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
