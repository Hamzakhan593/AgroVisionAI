namespace AgroVisionAI.Models.WeatherRisk
{
    public enum DiseaseRiskLevel
    {
        Low,
        Moderate,
        High
    }

    public sealed class WeatherSnapshot
    {
        public string LocationName { get; init; } = string.Empty;
        public string? AdminArea { get; init; }
        public string Country { get; init; } = "Pakistan";
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public string TimeZone { get; init; } = string.Empty;
        public double CurrentTemperatureC { get; init; }
        public double CurrentHumidityPercent { get; init; }
        public double CurrentPrecipitationMm { get; init; }
        public double Next24HoursAverageHumidityPercent { get; init; }
        public double Next24HoursMinimumTemperatureC { get; init; }
        public double Next24HoursMaximumTemperatureC { get; init; }
        public double Next24HoursPrecipitationMm { get; init; }
        public double Next72HoursPrecipitationMm { get; init; }
        public double Next72HoursMaximumPrecipitationProbabilityPercent { get; init; }
        public DateTime RetrievedAtUtc { get; init; } = DateTime.UtcNow;

        public string DisplayLocation => string.IsNullOrWhiteSpace(AdminArea)
            ? $"{LocationName}, {Country}"
            : $"{LocationName}, {AdminArea}, {Country}";
    }

    public sealed class DiseaseRiskAssessment
    {
        public string Disease { get; init; } = string.Empty;
        public DiseaseRiskLevel Level { get; init; }
        public int Score { get; init; }
        public string Summary { get; init; } = string.Empty;
        public bool IsWeatherProxyOnly { get; init; }
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Advice { get; init; } = Array.Empty<string>();
    }

    public sealed class WeatherRiskViewModel
    {
        public string LocationQuery { get; set; } = string.Empty;
        public string Crop { get; set; } = "Wheat";
        public WeatherSnapshot? Weather { get; set; }
        public IReadOnlyList<DiseaseRiskAssessment> Assessments { get; set; } = Array.Empty<DiseaseRiskAssessment>();
        public string? ErrorMessage { get; set; }
        public IReadOnlyList<string> SupportedCrops { get; } = new[] { "Cotton", "Wheat", "Rice" };

        public DiseaseRiskAssessment? HighestRisk => Assessments
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();
    }
}
