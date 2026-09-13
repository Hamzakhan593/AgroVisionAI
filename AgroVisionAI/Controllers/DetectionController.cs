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
    public class DetectionController : Controller
    {
        private static readonly IReadOnlyDictionary<string, string> SupportedCrops =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cotton"] = "Cotton",
                ["Wheat"] = "Wheat",
                ["Rice"] = "Rice"
            };

        private static readonly IReadOnlyDictionary<string, string[]> FeedbackDiseaseOptions =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cotton"] = new[] { "Bacterial Blight", "Cotton Leaf Curl Virus", "Healthy Cotton" },
                ["Wheat"] = new[] { "Wheat Brown Rust", "Wheat Yellow Rust", "Healthy Wheat" },
                ["Rice"] = new[] { "Rice Bacterial Leaf Blight", "Rice Brown Spot", "Rice Leaf Blast", "Rice Tungro", "Healthy Rice" }
            };

        private readonly IWebHostEnvironment _environment;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAgroVisionApiClient _aiClient;
        private readonly IDiagnosticReportService _diagnosticReportService;
        private readonly ILogger<DetectionController> _logger;

        public DetectionController(
            IWebHostEnvironment environment,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IAgroVisionApiClient aiClient,
            IDiagnosticReportService diagnosticReportService,
            ILogger<DetectionController> logger)
        {
            _environment = environment;
            _context = context;
            _userManager = userManager;
            _aiClient = aiClient;
            _diagnosticReportService = diagnosticReportService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Analyze(
            IFormFile cropImage,
            CancellationToken cancellationToken)
        {
            var crop = "automatic";

            if (cropImage == null || cropImage.Length == 0)
            {
                TempData["DetectionError"] = "Please upload an image.";
                return RedirectToAction(nameof(Index));
            }

            const long maxFileSize = 5 * 1024 * 1024;
            if (cropImage.Length > maxFileSize)
            {
                TempData["DetectionError"] = "The image must be 5 MB or smaller.";
                return RedirectToAction(nameof(Index));
            }

            var extension = Path.GetExtension(cropImage.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            if (!allowedExtensions.Contains(extension))
            {
                TempData["DetectionError"] = "Only JPG, JPEG, and PNG images are allowed.";
                return RedirectToAction(nameof(Index));
            }

            if (!await IsValidImageFile(cropImage, extension))
            {
                TempData["DetectionError"] = "The uploaded file is not a valid JPG, JPEG, or PNG image.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["DetectionError"] = "Your session has expired. Please sign in again.";
                return RedirectToAction("Login", "Account");
            }

            var safeExtension = extension == ".png" ? ".png" : ".jpg";
            var uploadsFolder = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "CropImages");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid():N}{safeExtension}";
            var filePath = Path.Combine(uploadsFolder, fileName);
            var keepImage = false;

            try
            {
                await using (var stream = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true))
                {
                    await cropImage.CopyToAsync(stream, cancellationToken);
                }

                var prediction = await _aiClient.PredictAsync(
                    filePath,
                    cropImage.FileName,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(prediction.Crop) ||
                    !SupportedCrops.TryGetValue(prediction.Crop.Trim(), out var detectedCrop))
                {
                    throw new AgroVisionApiException("The service did not identify a supported crop. Please try a clearer leaf image.");
                }
                crop = detectedCrop;

                var disease = await _context.Diseases.FirstOrDefaultAsync(
                    item => item.Crop == crop && item.Name == prediction.Disease,
                    cancellationToken);

                if (disease == null)
                {
                    disease = new Disease
                    {
                        Name = prediction.Disease,
                        Crop = crop,
                        Description = prediction.Description,
                        Symptoms = prediction.Symptoms,
                        Treatment = prediction.Treatment,
                        Prevention = prediction.Prevention
                    };
                    _context.Diseases.Add(disease);
                }

                var diseaseConfidence = Math.Clamp(prediction.Confidence, 0.0, 1.0);
                var secondPrediction = prediction.TopPredictions
                    .Skip(1)
                    .FirstOrDefault(item => double.IsFinite(item.Confidence));
                var secondConfidence = secondPrediction == null
                    ? (double?)null
                    : Math.Clamp(secondPrediction.Confidence, 0.0, 1.0);
                var confidenceMargin = secondConfidence.HasValue
                    ? Math.Max(0.0, diseaseConfidence - secondConfidence.Value)
                    : (double?)null;

                var detection = new Detection
                {
                    UserId = user.Id,
                    Crop = crop,
                    ImagePath = fileName,
                    Disease = disease,
                    Confidence = diseaseConfidence,
                    RequestId = Truncate(prediction.RequestId, 64),
                    CropConfidence = Math.Clamp(prediction.CropConfidence!.Value, 0.0, 1.0),
                    CropModelName = Truncate(prediction.CropModelName, 200),
                    CropModelVersion = Truncate(prediction.CropModelVersion, 50),
                    PredictedClass = Truncate(prediction.PredictedClass, 100),
                    DiseaseModelName = Truncate(prediction.ModelName, 200),
                    DiseaseModelVersion = Truncate(prediction.ModelVersion, 50),
                    SecondPredictedClass = Truncate(secondPrediction?.Label, 100),
                    SecondPredictionName = Truncate(secondPrediction?.Name, 150),
                    SecondConfidence = secondConfidence,
                    ConfidenceMargin = confidenceMargin,
                    ProcessingTimeMs = prediction.ProcessingTimeMs,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Detections.Add(detection);
                await _context.SaveChangesAsync(cancellationToken);
                keepImage = true;

                // Grad-CAM is supporting explainability only. If its image cannot be
                // stored, keep the valid prediction and continue without the overlay.
                if (prediction.ExplanationAvailable &&
                    !string.IsNullOrWhiteSpace(prediction.ExplanationImageBase64))
                {
                    try
                    {
                        await SaveGradCamAsync(
                            detection.Id,
                            prediction.ExplanationImageBase64,
                            prediction.ExplanationImageMediaType,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exc)
                    {
                        _logger.LogWarning(
                            exc,
                            "Grad-CAM explanation could not be stored for detection {DetectionId}.",
                            detection.Id);
                    }
                }

                return RedirectToAction(nameof(Result), new { id = detection.Id });
            }
            catch (AgroVisionApiException exc)
            {
                _logger.LogWarning(exc, "AI prediction failed for crop {Crop}.", crop);
                TempData["DetectionError"] = exc.Message;
                return RedirectToAction(nameof(Index));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new StatusCodeResult(499);
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "Crop analysis could not be saved for crop {Crop}.", crop);
                TempData["DetectionError"] = "The analysis could not be completed. Please try again.";
                return RedirectToAction(nameof(Index));
            }
            finally
            {
                if (!keepImage)
                {
                    TryDeleteFile(filePath);
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> Result(int id, int? daysToHarvest = null, bool? whiteflyTreatmentNeeded = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var detection = await _context.Detections
                .Include(item => item.Disease)
                .FirstOrDefaultAsync(item => item.Id == id && item.UserId == user.Id);

            if (detection == null) return NotFound();
            if (!ModelState.IsValid || daysToHarvest is < 0 or > 365)
            {
                ViewData["FieldContextError"] = "Enter whole days to harvest between 0 and 365. The field context was not applied.";
                daysToHarvest = -1; // Fail closed: invalid context must not expose an applicable dose.
                whiteflyTreatmentNeeded = null;
            }
            var feedback = await _context.PredictionFeedbacks
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.DetectionId == detection.Id && item.UserId == user.Id);

            var correctionOptions = FeedbackDiseaseOptions.TryGetValue(detection.Crop, out var cropOptions)
                ? cropOptions
                    .Where(name => !string.Equals(name, detection.Disease?.Name, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<string>();

            ViewData["DaysToHarvest"] = daysToHarvest;
            ViewData["WhiteflyTreatmentNeeded"] = whiteflyTreatmentNeeded;
            ViewData["PredictionFeedback"] = feedback;
            ViewData["FeedbackDiseaseOptions"] = correctionOptions;
            ViewData["GradCamAvailable"] = System.IO.File.Exists(GetGradCamPath(detection.Id));
            return View(detection);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitFeedback(
            int detectionId,
            FeedbackVerdict verdict,
            string? suggestedDisease,
            string? comment,
            CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var detection = await _context.Detections
                .Include(item => item.Disease)
                .FirstOrDefaultAsync(
                    item => item.Id == detectionId && item.UserId == user.Id,
                    cancellationToken);

            if (detection == null)
            {
                return NotFound();
            }

            if (!Enum.IsDefined(typeof(FeedbackVerdict), verdict))
            {
                TempData["FeedbackError"] = "Please choose whether the AI result was correct, incorrect, or you are not sure.";
                return RedirectToAction(nameof(Result), new { id = detectionId });
            }

            comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            if (comment?.Length > 500)
            {
                TempData["FeedbackError"] = "Feedback notes must be 500 characters or fewer.";
                return RedirectToAction(nameof(Result), new { id = detectionId });
            }

            suggestedDisease = string.IsNullOrWhiteSpace(suggestedDisease)
                ? null
                : suggestedDisease.Trim();

            if (verdict == FeedbackVerdict.Incorrect)
            {
                if (suggestedDisease != null)
                {
                    var validCorrection = FeedbackDiseaseOptions.TryGetValue(detection.Crop, out var cropOptions) &&
                        cropOptions.Any(name =>
                            string.Equals(name, suggestedDisease, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(name, detection.Disease?.Name, StringComparison.OrdinalIgnoreCase));

                    if (!validCorrection)
                    {
                        TempData["FeedbackError"] = "The corrected condition is not valid for the detected crop.";
                        return RedirectToAction(nameof(Result), new { id = detectionId });
                    }

                    // Save the canonical display name rather than arbitrary submitted casing.
                    suggestedDisease = cropOptions!.First(name =>
                        string.Equals(name, suggestedDisease, StringComparison.OrdinalIgnoreCase));
                }
            }
            else
            {
                // A correction only makes sense when the user says the prediction is incorrect.
                suggestedDisease = null;
            }

            var feedback = await _context.PredictionFeedbacks.FirstOrDefaultAsync(
                item => item.DetectionId == detectionId && item.UserId == user.Id,
                cancellationToken);

            var now = DateTime.UtcNow;
            var isNew = feedback == null;

            if (feedback == null)
            {
                feedback = new PredictionFeedback
                {
                    DetectionId = detectionId,
                    UserId = user.Id,
                    CreatedAt = now
                };
                _context.PredictionFeedbacks.Add(feedback);
            }

            feedback.Verdict = verdict;
            feedback.SuggestedDisease = suggestedDisease;
            feedback.Comment = comment;
            feedback.UpdatedAt = now;

            // Any user edit invalidates a previous review so the latest feedback is
            // always re-checked before it can become a verified training candidate.
            feedback.VerificationStatus = FeedbackVerificationStatus.Pending;
            feedback.VerifiedDisease = null;
            feedback.ReviewerNote = null;
            feedback.VerifiedAt = null;

            await _context.SaveChangesAsync(cancellationToken);

            TempData["FeedbackSuccess"] = isNew
                ? "Thanks. Your feedback was saved for model-quality review."
                : "Your feedback was updated and returned to the review queue.";

            return RedirectToAction(nameof(Result), new { id = detectionId });
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ExportReport(
            int id,
            int? daysToHarvest = null,
            bool? whiteflyTreatmentNeeded = null,
            CancellationToken cancellationToken = default)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (daysToHarvest is < 0 or > 365)
            {
                TempData["ReportError"] = "Days to harvest must be between 0 and 365.";
                return RedirectToAction(nameof(Result), new { id });
            }

            var detection = await _context.Detections
                .AsNoTracking()
                .Include(item => item.Disease)
                .FirstOrDefaultAsync(
                    item => item.Id == id && item.UserId == user.Id,
                    cancellationToken);

            if (detection == null) return NotFound();

            try
            {
                var pdf = await _diagnosticReportService.GenerateAsync(
                    detection,
                    user,
                    daysToHarvest,
                    whiteflyTreatmentNeeded,
                    cancellationToken);

                return File(
                    pdf,
                    "application/pdf",
                    $"AgroVisionAI_Diagnostic_Report_{detection.Id}.pdf");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new StatusCodeResult(499);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnostic PDF report generation failed for detection {DetectionId}.", id);
                TempData["ReportError"] = "The PDF report could not be generated. Please try again.";
                return RedirectToAction(nameof(Result), new
                {
                    id,
                    daysToHarvest,
                    whiteflyTreatmentNeeded
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Image(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            var detection = await _context.Detections.FirstOrDefaultAsync(
                item => item.Id == id && item.UserId == user.Id);
            if (detection == null)
            {
                return NotFound();
            }

            var imagePath = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "CropImages",
                Path.GetFileName(detection.ImagePath));
            if (!System.IO.File.Exists(imagePath))
            {
                return NotFound();
            }

            var contentType = Path.GetExtension(imagePath).ToLowerInvariant() == ".png"
                ? "image/png"
                : "image/jpeg";
            return PhysicalFile(imagePath, contentType);
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ExplanationImage(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            var ownsDetection = await _context.Detections
                .AsNoTracking()
                .AnyAsync(item => item.Id == id && item.UserId == user.Id);
            if (!ownsDetection)
            {
                return NotFound();
            }

            var explanationPath = GetGradCamPath(id);
            if (!System.IO.File.Exists(explanationPath))
            {
                return NotFound();
            }

            return PhysicalFile(explanationPath, "image/jpeg");
        }

        private async Task SaveGradCamAsync(
            int detectionId,
            string imageBase64,
            string? mediaType,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Unsupported Grad-CAM image format.");
            }

            // A 900px JPEG should be far below this. Keep a hard ceiling so an
            // unexpectedly large upstream payload is never written to disk.
            const int maxBase64Chars = 4_000_000;
            if (imageBase64.Length > maxBase64Chars)
            {
                throw new InvalidDataException("Grad-CAM image payload is too large.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(imageBase64);
            }
            catch (FormatException exc)
            {
                throw new InvalidDataException("Grad-CAM image payload is invalid.", exc);
            }

            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
            {
                throw new InvalidDataException("Grad-CAM payload is not a valid JPEG image.");
            }

            var folder = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "Explainability");
            Directory.CreateDirectory(folder);

            var path = GetGradCamPath(detectionId);
            await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }

        private string GetGradCamPath(int detectionId)
        {
            return Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "Explainability",
                $"{detectionId}.jpg");
        }

        private static async Task<bool> IsValidImageFile(IFormFile file, string extension)
        {
            await using var stream = file.OpenReadStream();
            var header = new byte[8];
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length));
            if (bytesRead < 8)
            {
                return false;
            }

            if (extension is ".jpg" or ".jpeg")
            {
                return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            }

            return extension == ".png" &&
                   header[0] == 0x89 && header[1] == 0x50 &&
                   header[2] == 0x4E && header[3] == 0x47 &&
                   header[4] == 0x0D && header[5] == 0x0A &&
                   header[6] == 0x1A && header[7] == 0x0A;
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch
            {
                // A failed cleanup must not hide the original analysis error.
            }
        }
    }
}
