using AgroVisionAI.Data;
using AgroVisionAI.Models;
using AgroVisionAI.Models.Api.Mobile;
using AgroVisionAI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroVisionAI.Controllers.Api
{
    [ApiController]
    [Route("api/mobile/detections")]
    [Authorize(Policy = "ApiUser")]
    public sealed class DetectionsApiController : ControllerBase
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
        private readonly ILogger<DetectionsApiController> _logger;

        public DetectionsApiController(
            IWebHostEnvironment environment,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IAgroVisionApiClient aiClient,
            IDiagnosticReportService diagnosticReportService,
            ILogger<DetectionsApiController> logger)
        {
            _environment = environment;
            _context = context;
            _userManager = userManager;
            _aiClient = aiClient;
            _diagnosticReportService = diagnosticReportService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<DetectionListItemResponse>>> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var detections = await _context.Detections
                .AsNoTracking()
                .Include(item => item.Disease)
                .Where(item => item.UserId == user.Id)
                .OrderByDescending(item => item.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Ok(detections.Select(ToListItem).ToArray());
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<DetectionDetailResponse>> Get(
            int id,
            CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var detection = await _context.Detections
                .AsNoTracking()
                .Include(item => item.Disease)
                .FirstOrDefaultAsync(
                    item => item.Id == id && item.UserId == user.Id,
                    cancellationToken);

            if (detection == null) return NotFound();

            var feedback = await _context.PredictionFeedbacks
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.DetectionId == id && item.UserId == user.Id,
                    cancellationToken);

            return Ok(ToDetail(detection, feedback));
        }

        [HttpPost("analyze")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<ActionResult<DetectionDetailResponse>> Analyze(
            [FromForm] IFormFile cropImage,
            CancellationToken cancellationToken)
        {
            if (cropImage == null || cropImage.Length == 0)
            {
                return BadRequest(new { message = "Please upload an image." });
            }

            const long maxFileSize = 5 * 1024 * 1024;
            if (cropImage.Length > maxFileSize)
            {
                return BadRequest(new { message = "The image must be 5 MB or smaller." });
            }

            var extension = Path.GetExtension(cropImage.FileName).ToLowerInvariant();
            if (extension is not (".jpg" or ".jpeg" or ".png"))
            {
                return BadRequest(new { message = "Only JPG, JPEG, and PNG images are allowed." });
            }

            if (!await IsValidImageFile(cropImage, extension))
            {
                return BadRequest(new { message = "The uploaded file is not a valid JPG, JPEG, or PNG image." });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

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
                    return UnprocessableEntity(new
                    {
                        message = "The AI service did not identify a supported crop. Please try a clearer leaf image."
                    });
                }

                var disease = await _context.Diseases.FirstOrDefaultAsync(
                    item => item.Crop == detectedCrop && item.Name == prediction.Disease,
                    cancellationToken);

                if (disease == null)
                {
                    disease = new Disease
                    {
                        Name = prediction.Disease,
                        Crop = detectedCrop,
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
                    Crop = detectedCrop,
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
                            "Grad-CAM explanation could not be stored for API detection {DetectionId}.",
                            detection.Id);
                    }
                }

                return CreatedAtAction(
                    nameof(Get),
                    new { id = detection.Id },
                    ToDetail(detection, null));
            }
            catch (AgroVisionApiException exc)
            {
                _logger.LogWarning(exc, "Mobile API AI prediction failed.");
                var status = exc.StatusCode is >= 400 and < 500
                    ? StatusCodes.Status422UnprocessableEntity
                    : StatusCodes.Status503ServiceUnavailable;
                return StatusCode(status, new { message = exc.Message });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return StatusCode(499);
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "Mobile API crop analysis could not be saved.");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { message = "The analysis could not be completed. Please try again." });
            }
            finally
            {
                if (!keepImage)
                {
                    TryDeleteFile(filePath);
                }
            }
        }

        [HttpPost("{id:int}/feedback")]
        public async Task<ActionResult<PredictionFeedbackResponse>> SaveFeedback(
            int id,
            [FromBody] PredictionFeedbackRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var detection = await _context.Detections
                .Include(item => item.Disease)
                .FirstOrDefaultAsync(
                    item => item.Id == id && item.UserId == user.Id,
                    cancellationToken);

            if (detection == null) return NotFound();

            if (!Enum.IsDefined(typeof(FeedbackVerdict), request.Verdict))
            {
                return BadRequest(new { message = "Invalid feedback verdict." });
            }

            var comment = string.IsNullOrWhiteSpace(request.Comment)
                ? null
                : request.Comment.Trim();
            if (comment?.Length > 500)
            {
                return BadRequest(new { message = "Feedback notes must be 500 characters or fewer." });
            }

            var suggestedDisease = string.IsNullOrWhiteSpace(request.SuggestedDisease)
                ? null
                : request.SuggestedDisease.Trim();

            if (request.Verdict == FeedbackVerdict.Incorrect && suggestedDisease != null)
            {
                var validCorrection = FeedbackDiseaseOptions.TryGetValue(detection.Crop, out var cropOptions) &&
                    cropOptions.Any(name =>
                        string.Equals(name, suggestedDisease, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, detection.Disease?.Name, StringComparison.OrdinalIgnoreCase));

                if (!validCorrection)
                {
                    return BadRequest(new { message = "The corrected condition is not valid for the detected crop." });
                }

                suggestedDisease = cropOptions!.First(name =>
                    string.Equals(name, suggestedDisease, StringComparison.OrdinalIgnoreCase));
            }
            else if (request.Verdict != FeedbackVerdict.Incorrect)
            {
                suggestedDisease = null;
            }

            var feedback = await _context.PredictionFeedbacks.FirstOrDefaultAsync(
                item => item.DetectionId == id && item.UserId == user.Id,
                cancellationToken);

            var now = DateTime.UtcNow;
            if (feedback == null)
            {
                feedback = new PredictionFeedback
                {
                    DetectionId = id,
                    UserId = user.Id,
                    CreatedAt = now
                };
                _context.PredictionFeedbacks.Add(feedback);
            }

            feedback.Verdict = request.Verdict;
            feedback.SuggestedDisease = suggestedDisease;
            feedback.Comment = comment;
            feedback.UpdatedAt = now;
            feedback.VerificationStatus = FeedbackVerificationStatus.Pending;
            feedback.VerifiedDisease = null;
            feedback.ReviewerNote = null;
            feedback.VerifiedAt = null;

            await _context.SaveChangesAsync(cancellationToken);
            return Ok(ToFeedback(feedback));
        }

        [HttpGet("{id:int}/image")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Image(int id, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var detection = await _context.Detections
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.Id == id && item.UserId == user.Id,
                    cancellationToken);
            if (detection == null) return NotFound();

            var imagePath = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "CropImages",
                Path.GetFileName(detection.ImagePath));
            if (!System.IO.File.Exists(imagePath)) return NotFound();

            var contentType = Path.GetExtension(imagePath).ToLowerInvariant() == ".png"
                ? "image/png"
                : "image/jpeg";
            return PhysicalFile(imagePath, contentType);
        }

        [HttpGet("{id:int}/report.pdf")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ReportPdf(
            int id,
            [FromQuery] int? daysToHarvest = null,
            [FromQuery] bool? whiteflyTreatmentNeeded = null,
            CancellationToken cancellationToken = default)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (daysToHarvest is < 0 or > 365)
                return BadRequest(new { message = "daysToHarvest must be between 0 and 365." });

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
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile diagnostic PDF report generation failed for detection {DetectionId}.", id);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { message = "The PDF report could not be generated." });
            }
        }

        [HttpGet("{id:int}/explanation")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Explanation(int id, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var ownsDetection = await _context.Detections
                .AsNoTracking()
                .AnyAsync(item => item.Id == id && item.UserId == user.Id, cancellationToken);
            if (!ownsDetection) return NotFound();

            var path = GetGradCamPath(id);
            return System.IO.File.Exists(path)
                ? PhysicalFile(path, "image/jpeg")
                : NotFound();
        }

        private DetectionListItemResponse ToListItem(Detection detection)
        {
            return new DetectionListItemResponse
            {
                Id = detection.Id,
                Crop = detection.Crop,
                Disease = detection.Disease?.Name ?? "Unknown",
                Confidence = detection.Confidence,
                CropConfidence = detection.CropConfidence,
                CreatedAtUtc = detection.CreatedAt,
                ImageUrl = $"/api/mobile/detections/{detection.Id}/image",
                ExplanationUrl = System.IO.File.Exists(GetGradCamPath(detection.Id))
                    ? $"/api/mobile/detections/{detection.Id}/explanation"
                    : null,
                ReportUrl = $"/api/mobile/detections/{detection.Id}/report.pdf"
            };
        }

        private DetectionDetailResponse ToDetail(Detection detection, PredictionFeedback? feedback)
        {
            var options = FeedbackDiseaseOptions.TryGetValue(detection.Crop, out var cropOptions)
                ? cropOptions
                    .Where(name => !string.Equals(name, detection.Disease?.Name, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<string>();

            return new DetectionDetailResponse
            {
                Id = detection.Id,
                Crop = detection.Crop,
                Disease = detection.Disease?.Name ?? "Unknown",
                Confidence = detection.Confidence,
                CropConfidence = detection.CropConfidence,
                CreatedAtUtc = detection.CreatedAt,
                ImageUrl = $"/api/mobile/detections/{detection.Id}/image",
                ExplanationUrl = System.IO.File.Exists(GetGradCamPath(detection.Id))
                    ? $"/api/mobile/detections/{detection.Id}/explanation"
                    : null,
                ReportUrl = $"/api/mobile/detections/{detection.Id}/report.pdf",
                RequestId = detection.RequestId,
                PredictedClass = detection.PredictedClass,
                CropModelName = detection.CropModelName,
                CropModelVersion = detection.CropModelVersion,
                DiseaseModelName = detection.DiseaseModelName,
                DiseaseModelVersion = detection.DiseaseModelVersion,
                SecondPredictionName = detection.SecondPredictionName,
                SecondConfidence = detection.SecondConfidence,
                ConfidenceMargin = detection.ConfidenceMargin,
                ProcessingTimeMs = detection.ProcessingTimeMs,
                Description = detection.Disease?.Description ?? string.Empty,
                Symptoms = detection.Disease?.Symptoms ?? string.Empty,
                Treatment = detection.Disease?.Treatment ?? string.Empty,
                Prevention = detection.Disease?.Prevention ?? string.Empty,
                Feedback = feedback == null ? null : ToFeedback(feedback),
                CorrectionOptions = options
            };
        }

        private static PredictionFeedbackResponse ToFeedback(PredictionFeedback feedback)
        {
            return new PredictionFeedbackResponse
            {
                Verdict = feedback.Verdict,
                SuggestedDisease = feedback.SuggestedDisease,
                Comment = feedback.Comment,
                VerificationStatus = feedback.VerificationStatus,
                UpdatedAtUtc = feedback.UpdatedAt
            };
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

            await System.IO.File.WriteAllBytesAsync(
                GetGradCamPath(detectionId),
                bytes,
                cancellationToken);
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
            if (bytesRead < 8) return false;

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
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch
            {
                // Cleanup must not hide the original analysis error.
            }
        }
    }
}
