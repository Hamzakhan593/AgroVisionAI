using System.Security.Claims;
using AgroVisionAI.Data;
using AgroVisionAI.Models;
using AgroVisionAI.Models.Api;
using AgroVisionAI.Models.ModelRegistry;
using AgroVisionAI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroVisionAI.Controllers
{
    [Authorize(Roles = "Admin")]
    public sealed class ModelRegistryController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IAgroVisionApiClient _api;
        private readonly ILogger<ModelRegistryController> _logger;

        public ModelRegistryController(
            ApplicationDbContext db,
            IAgroVisionApiClient api,
            ILogger<ModelRegistryController> logger)
        {
            _db = db;
            _api = api;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            bool reachable = false;
            string? message = null;
            try
            {
                var runtimeModels = await _api.GetManagedModelsAsync(cancellationToken);
                await SyncRegistryAsync(runtimeModels, cancellationToken);
                reachable = true;
                message = "AI runtime registry synchronized.";
            }
            catch (AgroVisionApiException ex)
            {
                message = ex.Message;
                _logger.LogWarning(ex, "Could not synchronize the AI model registry.");
            }

            var models = await _db.AiModelRegistryEntries
                .AsNoTracking()
                .OrderBy(item => item.Crop)
                .ThenByDescending(item => item.Status == AiModelLifecycleStatus.Active)
                .ThenByDescending(item => item.Version)
                .ThenBy(item => item.Filename)
                .ToListAsync(cancellationToken);

            return View(new ModelRegistryIndexViewModel
            {
                Models = models,
                AiServiceReachable = reachable,
                ServiceMessage = message
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
        {
            var target = await _db.AiModelRegistryEntries.FindAsync(new object[] { id }, cancellationToken);
            if (target == null)
                return NotFound();

            try
            {
                var runtime = await _api.ActivateManagedModelAsync(
                    target.Crop,
                    target.Filename,
                    cancellationToken);

                var sameCrop = await _db.AiModelRegistryEntries
                    .Where(item => item.Crop == target.Crop)
                    .ToListAsync(cancellationToken);

                var now = DateTime.UtcNow;
                foreach (var item in sameCrop)
                {
                    if (item.Id == target.Id)
                    {
                        item.Status = AiModelLifecycleStatus.Active;
                        item.IsAvailable = runtime.Available;
                        item.IsLoaded = runtime.Loaded;
                        item.Preprocessing = runtime.Preprocessing;
                        item.SelectionLockedByEnvironment = runtime.SelectionLockedByEnvironment;
                        item.ActivatedAtUtc = now;
                        item.LastActivatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    }
                    else if (item.Status == AiModelLifecycleStatus.Active)
                    {
                        item.Status = AiModelLifecycleStatus.Archived;
                        item.IsLoaded = false;
                    }
                    item.UpdatedAtUtc = now;
                }

                await _db.SaveChangesAsync(cancellationToken);
                TempData["ModelRegistrySuccess"] = $"{target.Crop.TitleCase()} {target.Version} is now active. New predictions will use {target.Filename}.";
            }
            catch (AgroVisionApiException ex)
            {
                TempData["ModelRegistryError"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id, CancellationToken cancellationToken)
        {
            var item = await _db.AiModelRegistryEntries.FindAsync(new object[] { id }, cancellationToken);
            if (item == null)
                return NotFound();
            if (item.Status == AiModelLifecycleStatus.Active)
            {
                TempData["ModelRegistryError"] = "The active model cannot be archived. Activate another model for this crop first.";
                return RedirectToAction(nameof(Index));
            }

            item.Status = AiModelLifecycleStatus.Archived;
            item.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            TempData["ModelRegistrySuccess"] = $"{item.Filename} archived in the registry.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkCandidate(int id, CancellationToken cancellationToken)
        {
            var item = await _db.AiModelRegistryEntries.FindAsync(new object[] { id }, cancellationToken);
            if (item == null)
                return NotFound();
            if (item.Status != AiModelLifecycleStatus.Active)
            {
                item.Status = AiModelLifecycleStatus.Candidate;
                item.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveMetadata(
            UpdateModelMetadataViewModel model,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                TempData["ModelRegistryError"] = "Metrics must be between 0 and 1 and notes must stay within the allowed length.";
                return RedirectToAction(nameof(Index));
            }

            var item = await _db.AiModelRegistryEntries.FindAsync(new object[] { model.Id }, cancellationToken);
            if (item == null)
                return NotFound();

            item.Architecture = Clean(model.Architecture);
            item.Accuracy = model.Accuracy;
            item.MacroF1 = model.MacroF1;
            item.ExternalAccuracy = model.ExternalAccuracy;
            item.Notes = Clean(model.Notes);
            item.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            TempData["ModelRegistrySuccess"] = "Model metadata updated.";
            return RedirectToAction(nameof(Index));
        }

        private async Task SyncRegistryAsync(
            IReadOnlyList<AiManagedModelStatus> runtimeModels,
            CancellationToken cancellationToken)
        {
            var existing = await _db.AiModelRegistryEntries.ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;

            foreach (var runtime in runtimeModels)
            {
                var item = existing.FirstOrDefault(entry =>
                    entry.Crop.Equals(runtime.Crop, StringComparison.OrdinalIgnoreCase) &&
                    entry.Filename.Equals(runtime.Filename, StringComparison.OrdinalIgnoreCase));

                if (item == null)
                {
                    item = CreateEntry(runtime, now);
                    _db.AiModelRegistryEntries.Add(item);
                    existing.Add(item);
                }

                item.Version = runtime.Version;
                item.IsAvailable = runtime.Available;
                item.IsLoaded = runtime.Loaded;
                item.Preprocessing = runtime.Preprocessing;
                item.SelectionLockedByEnvironment = runtime.SelectionLockedByEnvironment;
                item.LastSeenAtUtc = now;
                item.UpdatedAtUtc = now;

                if (runtime.Active)
                {
                    foreach (var other in existing.Where(entry =>
                                 entry.Id != item.Id &&
                                 entry.Crop.Equals(runtime.Crop, StringComparison.OrdinalIgnoreCase) &&
                                 entry.Status == AiModelLifecycleStatus.Active))
                    {
                        other.Status = AiModelLifecycleStatus.Archived;
                        other.IsLoaded = false;
                        other.UpdatedAtUtc = now;
                    }
                    item.Status = AiModelLifecycleStatus.Active;
                    item.ActivatedAtUtc ??= now;
                }
                else if (item.Status == AiModelLifecycleStatus.Active)
                {
                    item.Status = AiModelLifecycleStatus.Archived;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private static AiModelRegistryEntry CreateEntry(AiManagedModelStatus runtime, DateTime now)
        {
            var entry = new AiModelRegistryEntry
            {
                Crop = runtime.Crop,
                Filename = runtime.Filename,
                Version = runtime.Version,
                Architecture = GuessArchitecture(runtime.Filename),
                Status = runtime.Active ? AiModelLifecycleStatus.Active : AiModelLifecycleStatus.Candidate,
                IsAvailable = runtime.Available,
                IsLoaded = runtime.Loaded,
                SelectionLockedByEnvironment = runtime.SelectionLockedByEnvironment,
                Preprocessing = runtime.Preprocessing,
                LastSeenAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ActivatedAtUtc = runtime.Active ? now : null
            };

            // Existing locked benchmark results from this FYP. Unknown future model files stay blank.
            switch (runtime.Filename.ToLowerInvariant())
            {
                case "cotton_cnn_v3_best.keras":
                    entry.Accuracy = 0.9185;
                    entry.MacroF1 = 0.9188;
                    entry.ExternalAccuracy = 0.5136;
                    break;
                case "wheat_cnn_v2_best.keras":
                    entry.Accuracy = 0.983725;
                    entry.MacroF1 = 0.9830;
                    entry.ExternalAccuracy = 0.466667;
                    break;
                case "rice_v2_efficientnetv2b0_best.keras":
                    entry.Accuracy = 0.900212;
                    entry.MacroF1 = 0.898463;
                    break;
            }

            return entry;
        }

        private static string GuessArchitecture(string filename) =>
            filename.Contains("efficientnet", StringComparison.OrdinalIgnoreCase)
                ? "EfficientNetV2B0"
                : "Custom CNN";

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static class ModelRegistryStringExtensions
    {
        public static string TitleCase(this string value) =>
            string.IsNullOrWhiteSpace(value)
                ? value
                : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
