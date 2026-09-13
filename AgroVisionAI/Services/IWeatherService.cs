using AgroVisionAI.Models.WeatherRisk;

namespace AgroVisionAI.Services
{
    public interface IWeatherService
    {
        Task<WeatherSnapshot> GetForecastAsync(
            string location,
            CancellationToken cancellationToken = default);
    }
}
