using AgroVisionAI.Models.WeatherRisk;
using AgroVisionAI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroVisionAI.Controllers
{
    [Authorize]
    public sealed class WeatherRiskController : Controller
    {
        private static readonly HashSet<string> SupportedCrops = new(StringComparer.OrdinalIgnoreCase)
        {
            "Cotton",
            "Wheat",
            "Rice"
        };

        private readonly IWeatherService _weatherService;
        private readonly IDiseaseRiskService _riskService;
        private readonly ILogger<WeatherRiskController> _logger;

        public WeatherRiskController(
            IWeatherService weatherService,
            IDiseaseRiskService riskService,
            ILogger<WeatherRiskController> logger)
        {
            _weatherService = weatherService;
            _riskService = riskService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(new WeatherRiskViewModel
            {
                Crop = "Wheat"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(
            WeatherRiskViewModel model,
            CancellationToken cancellationToken)
        {
            model.LocationQuery = (model.LocationQuery ?? string.Empty).Trim();
            model.Crop = (model.Crop ?? string.Empty).Trim();

            if (model.LocationQuery.Length < 2 || model.LocationQuery.Length > 120)
            {
                model.ErrorMessage = "Enter a Pakistan city or district name, for example Sanghar, Hyderabad or Multan.";
                return View(model);
            }

            if (!SupportedCrops.Contains(model.Crop))
            {
                model.ErrorMessage = "Select Cotton, Wheat or Rice.";
                return View(model);
            }

            try
            {
                model.Weather = await _weatherService.GetForecastAsync(
                    model.LocationQuery,
                    cancellationToken);
                model.Assessments = _riskService.Assess(model.Crop, model.Weather);
            }
            catch (WeatherLocationNotFoundException ex)
            {
                model.ErrorMessage = ex.Message;
            }
            catch (WeatherServiceException ex)
            {
                _logger.LogWarning(ex, "Weather risk request failed for {Location}.", model.LocationQuery);
                model.ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected weather risk error for {Location}.", model.LocationQuery);
                model.ErrorMessage = "Could not calculate weather disease risk right now. Please try again.";
            }

            return View(model);
        }
    }
}
