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

            const long maxFileSize = 5 * 1024 * 1024;

            if (cropImage.Length > maxFileSize)
            {
                TempData["DetectionError"] =
                    "The image must be smaller than 5 MB.";

                return RedirectToAction(nameof(Index));
            }

            var uploadsFolder = Path.Combine(
                _environment.WebRootPath,
                "uploads",
                "crop-images");

            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid():N}{extension}";

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
                ImagePath = $"/uploads/crop-images/{fileName}",
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


    }
}