using AgroVisionAI.Data;
using AgroVisionAI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroVisionAI.Controllers
{
    [Authorize]
    public class DetectionController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DetectionController(
            IWebHostEnvironment environment,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _environment = environment;
            _context = context;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }





        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Analyze(
            string selectedCrop,
            IFormFile cropImage)
        {
            if (string.IsNullOrWhiteSpace(selectedCrop))
            {
                TempData["DetectionError"] =
                    "Please select a crop.";

                return RedirectToAction(nameof(Index));
            }

            if (cropImage == null || cropImage.Length == 0)
            {
                TempData["DetectionError"] =
                    "Please upload an image.";

                return RedirectToAction(nameof(Index));
            }

            var allowedExtensions = new[]
            {
                ".jpg",
                ".jpeg",
                ".png"
            };

            var extension = Path
                .GetExtension(cropImage.FileName)
                .ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                TempData["DetectionError"] =
                    "Only JPG, JPEG, and PNG images are allowed.";

                return RedirectToAction(nameof(Index));
            }

            if (!await IsValidImageFile(cropImage, extension))
            {
                TempData["DetectionError"] =
                    "The uploaded file is not a valid JPG, JPEG, or PNG image.";

                return RedirectToAction(nameof(Index));
            }

            var safeExtension = extension switch
            {
                ".jpg" => ".jpg",
                ".jpeg" => ".jpg",
                ".png" => ".png",
                _ => throw new InvalidOperationException("Invalid image type.")
            };


            const long maxFileSize = 5 * 1024 * 1024;

            if (cropImage.Length > maxFileSize)
            {
                TempData["DetectionError"] =
                    "The image must be smaller than 5 MB.";

                return RedirectToAction(nameof(Index));
            }

             var uploadsFolder = Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "CropImages");

            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid():N}{safeExtension}";
            var filePath = Path.Combine(
                uploadsFolder,
                fileName);

            await using (var stream = new FileStream(
                filePath,
                FileMode.Create))
            {
                await cropImage.CopyToAsync(stream);
            }

            // Get the currently logged-in user
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                TempData["DetectionError"] =
                    "Your session has expired. Please sign in again.";

                return RedirectToAction("Login", "Account");
            }

            // Create database record
            var detection = new Detection
            {
                UserId = user.Id,
                Crop = selectedCrop,
                ImagePath = fileName,
                CreatedAt = DateTime.UtcNow
            };

            _context.Detections.Add(detection);

            await _context.SaveChangesAsync();

         return RedirectToAction(
         nameof(Result),
         new { id = detection.Id });
        }


        [HttpGet]
        public async Task<IActionResult> Result(int id)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var detection = await _context.Detections
                .FirstOrDefaultAsync(d =>
                    d.Id == id &&
                    d.UserId == user.Id);

            if (detection == null)
            {
                return NotFound();
            }

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

            var detection = await _context.Detections
                .FirstOrDefaultAsync(d =>
                    d.Id == id &&
                    d.UserId == user.Id);

            if (detection == null)
            {
                return NotFound();
            }

            var imagePath = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "CropImages",
                detection.ImagePath);

            if (!System.IO.File.Exists(imagePath))
            {
                return NotFound();
            }

            var extension = Path
                .GetExtension(imagePath)
                .ToLowerInvariant();

            var contentType = extension switch
            {
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                _ => "application/octet-stream"
            };

            return PhysicalFile(imagePath, contentType);
        }

        private static async Task<bool> IsValidImageFile(
        IFormFile file,
         string extension)
        {
            await using var stream = file.OpenReadStream();

            var header = new byte[8];

            var bytesRead = await stream.ReadAsync(header, 0, header.Length);

            if (bytesRead < 8)
            {
                return false;
            }

            // JPEG signature: FF D8 FF
            if (extension == ".jpg" || extension == ".jpeg")
            {
                return header[0] == 0xFF &&
                       header[1] == 0xD8 &&
                       header[2] == 0xFF;
            }

            // PNG signature:
            // 89 50 4E 47 0D 0A 1A 0A
            if (extension == ".png")
            {
                return header[0] == 0x89 &&
                       header[1] == 0x50 &&
                       header[2] == 0x4E &&
                       header[3] == 0x47 &&
                       header[4] == 0x0D &&
                       header[5] == 0x0A &&
                       header[6] == 0x1A &&
                       header[7] == 0x0A;
            }

            return false;
        }
    }
}