using AgroVisionAI.Models.Api.Mobile;
using AgroVisionAI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroVisionAI.Controllers.Api
{
    [ApiController]
    [Route("api/mobile/weather-risk")]
    [Authorize(Policy = "ApiUser")]
    public sealed class WeatherRiskApiController : ControllerBase
    {
        private static readonly HashSet<string> SupportedCrops = new(StringComparer.OrdinalIgnoreCase)
        {
            "Cotton",
            "Wheat",
            "Rice"
        };

        private readonly IWeatherService _weatherService;
        private readonly IDiseaseRiskService _riskService;

        public WeatherRiskApiController(
            IWeatherService weatherService,
            IDiseaseRiskService riskService)
        {
            _weatherService = weatherService;
            _riskService = riskService;
        }

        [HttpGet]
        public async Task<ActionResult<WeatherRiskApiResponse>> Get(
            [FromQuery] string location,
            [FromQuery] string crop,
            CancellationToken cancellationToken)
        {
            location = (location ?? string.Empty).Trim();
            crop = (crop ?? string.Empty).Trim();

            if (location.Length < 2 || location.Length > 120)
            {
                return BadRequest(new { message = "Enter a valid Pakistan city or district name." });
            }

            if (!SupportedCrops.Contains(crop))
            {
                return BadRequest(new { message = "Supported crops are Cotton, Wheat and Rice." });
            }

            try
            {
                var weather = await _weatherService.GetForecastAsync(location, cancellationToken);
                var risks = _riskService.Assess(crop, weather);

                return Ok(new WeatherRiskApiResponse(
                    weather.DisplayLocation,
                    crop,
                    new WeatherSummaryResponse(
                        weather.CurrentTemperatureC,
                        weather.CurrentHumidityPercent,
                        weather.Next24HoursAverageHumidityPercent,
                        weather.Next24HoursMinimumTemperatureC,
                        weather.Next24HoursMaximumTemperatureC,
                        weather.Next24HoursPrecipitationMm,
                        weather.Next72HoursPrecipitationMm,
                        weather.Next72HoursMaximumPrecipitationProbabilityPercent),
                    risks.Select(item => new DiseaseRiskItemResponse(
                        item.Disease,
                        item.Level.ToString(),
                        item.Score,
                        item.Summary,
                        item.IsWeatherProxyOnly,
                        item.Reasons,
                        item.Advice)).ToArray(),
                    "Weather risk is an environmental favorability estimate, not a diagnosis. Confirm symptoms in the field or with image analysis."));
            }
            catch (WeatherLocationNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (WeatherServiceException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
            }
        }
    }
}
