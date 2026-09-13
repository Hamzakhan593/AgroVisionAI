using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AgroVisionAI.Models.WeatherRisk;

namespace AgroVisionAI.Services
{
    public sealed class OpenMeteoWeatherService : IWeatherService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OpenMeteoWeatherService> _logger;
        private readonly string _geocodingBaseUrl;
        private readonly string _forecastBaseUrl;

        public OpenMeteoWeatherService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<OpenMeteoWeatherService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _geocodingBaseUrl = configuration["WeatherApi:GeocodingBaseUrl"]
                ?? "https://geocoding-api.open-meteo.com/v1/search";
            _forecastBaseUrl = configuration["WeatherApi:ForecastBaseUrl"]
                ?? "https://api.open-meteo.com/v1/forecast";
        }

        public async Task<WeatherSnapshot> GetForecastAsync(
            string location,
            CancellationToken cancellationToken = default)
        {
            var normalizedLocation = (location ?? string.Empty).Trim();
            if (normalizedLocation.Length < 2 || normalizedLocation.Length > 120)
            {
                throw new ArgumentException("Enter a valid Pakistan city or district name.", nameof(location));
            }

            var geocodingUrl =
                $"{_geocodingBaseUrl}?name={Uri.EscapeDataString(normalizedLocation)}&count=5&language=en&format=json&countryCode=PK";

            GeocodingResponse? geocoding;
            try
            {
                geocoding = await _httpClient.GetFromJsonAsync<GeocodingResponse>(
                    geocodingUrl,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Weather geocoding request failed for {Location}.", normalizedLocation);
                throw new WeatherServiceException("Weather location service is temporarily unavailable.", ex);
            }

            var place = geocoding?.Results?
                .FirstOrDefault(item => string.Equals(item.CountryCode, "PK", StringComparison.OrdinalIgnoreCase));

            if (place == null)
            {
                throw new WeatherLocationNotFoundException(
                    $"Could not find '{normalizedLocation}' in Pakistan. Try a nearby city or district name.");
            }

            var forecastUrl =
                $"{_forecastBaseUrl}?latitude={place.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $"&longitude={place.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                "&current=temperature_2m,relative_humidity_2m,precipitation,weather_code" +
                "&hourly=temperature_2m,relative_humidity_2m,precipitation_probability,precipitation" +
                "&forecast_hours=72&timezone=auto";

            ForecastResponse? forecast;
            try
            {
                forecast = await _httpClient.GetFromJsonAsync<ForecastResponse>(
                    forecastUrl,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Weather forecast request failed for {Latitude}, {Longitude}.",
                    place.Latitude,
                    place.Longitude);
                throw new WeatherServiceException("Weather forecast service is temporarily unavailable.", ex);
            }

            if (forecast?.Current == null || forecast.Hourly == null)
            {
                throw new WeatherServiceException("Weather provider returned an incomplete forecast.");
            }

            var temperatures = forecast.Hourly.Temperature2m ?? new List<double?>();
            var humidities = forecast.Hourly.RelativeHumidity2m ?? new List<double?>();
            var precipitation = forecast.Hourly.Precipitation ?? new List<double?>();
            var precipitationProbability = forecast.Hourly.PrecipitationProbability ?? new List<double?>();

            var next24Temperatures = temperatures.Take(24).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            var next24Humidity = humidities.Take(24).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            var next24Precipitation = precipitation.Take(24).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            var next72Precipitation = precipitation.Take(72).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            var next72Probability = precipitationProbability.Take(72).Where(value => value.HasValue).Select(value => value!.Value).ToArray();

            var currentTemperature = forecast.Current.Temperature2m ?? next24Temperatures.FirstOrDefault();
            var currentHumidity = forecast.Current.RelativeHumidity2m ?? next24Humidity.FirstOrDefault();

            return new WeatherSnapshot
            {
                LocationName = place.Name ?? normalizedLocation,
                AdminArea = place.Admin1,
                Country = place.Country ?? "Pakistan",
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                TimeZone = forecast.Timezone ?? place.Timezone ?? "Asia/Karachi",
                CurrentTemperatureC = currentTemperature,
                CurrentHumidityPercent = currentHumidity,
                CurrentPrecipitationMm = forecast.Current.Precipitation ?? 0,
                Next24HoursAverageHumidityPercent = next24Humidity.Length == 0 ? currentHumidity : next24Humidity.Average(),
                Next24HoursMinimumTemperatureC = next24Temperatures.Length == 0 ? currentTemperature : next24Temperatures.Min(),
                Next24HoursMaximumTemperatureC = next24Temperatures.Length == 0 ? currentTemperature : next24Temperatures.Max(),
                Next24HoursPrecipitationMm = next24Precipitation.Sum(),
                Next72HoursPrecipitationMm = next72Precipitation.Sum(),
                Next72HoursMaximumPrecipitationProbabilityPercent = next72Probability.Length == 0 ? 0 : next72Probability.Max(),
                RetrievedAtUtc = DateTime.UtcNow
            };
        }

        private sealed class GeocodingResponse
        {
            [JsonPropertyName("results")]
            public List<GeocodingResult>? Results { get; set; }
        }

        private sealed class GeocodingResult
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("latitude")]
            public double Latitude { get; set; }

            [JsonPropertyName("longitude")]
            public double Longitude { get; set; }

            [JsonPropertyName("country")]
            public string? Country { get; set; }

            [JsonPropertyName("country_code")]
            public string? CountryCode { get; set; }

            [JsonPropertyName("admin1")]
            public string? Admin1 { get; set; }

            [JsonPropertyName("timezone")]
            public string? Timezone { get; set; }
        }

        private sealed class ForecastResponse
        {
            [JsonPropertyName("timezone")]
            public string? Timezone { get; set; }

            [JsonPropertyName("current")]
            public ForecastCurrent? Current { get; set; }

            [JsonPropertyName("hourly")]
            public ForecastHourly? Hourly { get; set; }
        }

        private sealed class ForecastCurrent
        {
            [JsonPropertyName("temperature_2m")]
            public double? Temperature2m { get; set; }

            [JsonPropertyName("relative_humidity_2m")]
            public double? RelativeHumidity2m { get; set; }

            [JsonPropertyName("precipitation")]
            public double? Precipitation { get; set; }
        }

        private sealed class ForecastHourly
        {
            [JsonPropertyName("temperature_2m")]
            public List<double?>? Temperature2m { get; set; }

            [JsonPropertyName("relative_humidity_2m")]
            public List<double?>? RelativeHumidity2m { get; set; }

            [JsonPropertyName("precipitation_probability")]
            public List<double?>? PrecipitationProbability { get; set; }

            [JsonPropertyName("precipitation")]
            public List<double?>? Precipitation { get; set; }
        }
    }

    public sealed class WeatherLocationNotFoundException : Exception
    {
        public WeatherLocationNotFoundException(string message) : base(message)
        {
        }
    }

    public sealed class WeatherServiceException : Exception
    {
        public WeatherServiceException(string message) : base(message)
        {
        }

        public WeatherServiceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
