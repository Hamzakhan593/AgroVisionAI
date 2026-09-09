using AgroVisionAI.Data;
using AgroVisionAI.TreatmentPk;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AgroVisionAI.ViewComponents;

public sealed class TreatmentPkViewComponent : ViewComponent
{
    private readonly ApplicationDbContext db;
    private readonly IWebHostEnvironment environment;
    private readonly ILogger<TreatmentPkViewComponent> logger;
    public TreatmentPkViewComponent(ApplicationDbContext db, IWebHostEnvironment environment, ILogger<TreatmentPkViewComponent> logger)
    { this.db = db; this.environment = environment; this.logger = logger; }
    public async Task<IViewComponentResult> InvokeAsync(int detectionId, int? daysToHarvest = null, bool? whiteflyTreatmentNeeded = null)
    {
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Content("");
        // Do not trust a query-string disease, confidence or owner ID.
        var detection = await db.Detections.AsNoTracking().Include(d => d.Disease)
            .FirstOrDefaultAsync(d => d.Id == detectionId && d.UserId == userId);
        if (detection == null) return Content("");
        var page = new TreatmentPage { DetectionId = detectionId, Context = new FarmContext { DaysToHarvest = daysToHarvest, WhiteflyTreatmentNeeded = whiteflyTreatmentNeeded } };
        try
        {
            var store = new TreatmentStore(environment.ContentRootPath);
            page.Result = store.Engine.Evaluate(store.ReadPrediction(detection), page.Context);
        }
        catch (Exception ex)
        { logger.LogError(ex, "Treatment PK data/configuration could not be loaded."); }
        return View(page);
    }
}
