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

        private readonly IWebHostEnvironment _environment;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAgroVisionApiClient _aiClient;
        private readonly ILogger<DetectionController> _logger;

        public DetectionController(
            IWebHostEnvironment environment,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IAgroVisionApiClient aiClient,
            ILogger<DetectionController> logger)
        {
            _environment = environment;
            _context = context;
            _userManager = userManager;
            _aiClient = aiClient;
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

                var detection = new Detection
                {
                    UserId = user.Id,
                    Crop = crop,
                    ImagePath = fileName,
                    Disease = disease,
                    Confidence = Math.Clamp(prediction.Confidence, 0.0, 1.0),
                    CreatedAt = DateTime.UtcNow
                };

                _context.Detections.Add(detection);
                await _context.SaveChangesAsync(cancellationToken);
                keepImage = true;

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
            ViewData["DaysToHarvest"] = daysToHarvest;
            ViewData["WhiteflyTreatmentNeeded"] = whiteflyTreatmentNeeded;
            return View(detection);
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
