using AgroVisionAI.Models.WeatherRisk;

namespace AgroVisionAI.Services
{
    public interface IDiseaseRiskService
    {
        IReadOnlyList<DiseaseRiskAssessment> Assess(
            string crop,
            WeatherSnapshot weather);
    }
}
