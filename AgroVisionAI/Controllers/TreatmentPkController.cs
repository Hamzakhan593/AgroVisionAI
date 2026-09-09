using AgroVisionAI.Data;
using AgroVisionAI.TreatmentPk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AgroVisionAI.Controllers;

[Authorize]
[Route("TreatmentPk")]
public sealed class TreatmentPkController : Controller
{
    private readonly ApplicationDbContext db;
    private readonly IWebHostEnvironment environment;
    private readonly ILogger<TreatmentPkController> logger;
    public TreatmentPkController(ApplicationDbContext db, IWebHostEnvironment environment, ILogger<TreatmentPkController> logger)
    { this.db = db; this.environment = environment; this.logger = logger; }
    private TreatmentStore? Load()
    {
        try { return new TreatmentStore(environment.ContentRootPath); }
        catch (Exception ex) { logger.LogError(ex, "Treatment PK data/configuration error."); return null; }
    }
    [HttpGet("")]
    public IActionResult Index(string? crop, string? disease)
    {
        var store = Load();
        if (store == null) return View("Report", new TreatmentPage());
        ViewBag.Records = store.Catalog.Records;
        var page = new TreatmentPage();
        page.Result = string.IsNullOrWhiteSpace(crop) || string.IsNullOrWhiteSpace(disease)
            ? new TreatmentResult { State = "reference", Message = "Browse management guidance. This reference catalogue does not diagnose an image." }
            : store.Engine.Educational(crop, disease);
        return View(page);
    }
    [HttpGet("Report/{id:int}")]
    public async Task<IActionResult> Report(int id, string province = "", string stage = "", int? daysToHarvest = null, bool? whiteflyTreatmentNeeded = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var detection = await db.Detections.AsNoTracking().Include(d => d.Disease)
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
        if (detection == null) return NotFound();
        var farm = new FarmContext { Province = province, Stage = stage, DaysToHarvest = daysToHarvest, WhiteflyTreatmentNeeded = whiteflyTreatmentNeeded };
        if (!ModelState.IsValid || daysToHarvest is < 0 or > 365)
            return BadRequest("Enter whole days to harvest between 0 and 365, or leave the field blank.");
        var page = new TreatmentPage { DetectionId = id, Context = farm };
        var store = Load();
        if (store != null)
        {
            // Spray history is deliberately unknown in v1; this report is guidance-only until a reviewed
            // per-product application-history workflow is integrated. See docs/INTEGRATION.md.
            page.Result = store.Engine.Evaluate(store.ReadPrediction(detection), farm);
        }
        return View(page);
    }
    [Authorize(Roles = "Admin")]
    [HttpGet("Evidence")]
    public IActionResult Evidence()
    {
        var store = Load();
        if (store == null) return StatusCode(503, "Treatment evidence could not be loaded. Review server logs.");
        return View(new EvidencePage { Catalog = store.Catalog, Products = store.ProductFile.Products });
    }
}
