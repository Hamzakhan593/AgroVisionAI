using AgroVisionAI.Models.Api;

namespace AgroVisionAI.Services
{
    public interface IAgroVisionApiClient
    {
        Task<CropPredictionResponse> PredictAsync(
            string imagePath,
            string originalFileName,
            CancellationToken cancellationToken = default);
    }
}
