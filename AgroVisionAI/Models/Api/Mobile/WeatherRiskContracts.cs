namespace AgroVisionAI.Models.Api.Mobile
{
    public sealed record WeatherRiskApiResponse(
        string Location,
        string Crop,
        WeatherSummaryResponse Weather,
        IReadOnlyList<DiseaseRiskItemResponse> Risks,
        string Disclaimer);

    public sealed record WeatherSummaryResponse(
        double CurrentTemperatureC,
        double CurrentHumidityPercent,
        double Next24HoursAverageHumidityPercent,
        double Next24HoursMinimumTemperatureC,
        double Next24HoursMaximumTemperatureC,
        double Next24HoursPrecipitationMm,
        double Next72HoursPrecipitationMm,
        double Next72HoursMaximumPrecipitationProbabilityPercent);

    public sealed record DiseaseRiskItemResponse(
        string Disease,
        string Level,
        int Score,
        string Summary,
        bool WeatherProxyOnly,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<string> Advice);
}
