using AgroVisionAI.Models.Api;

namespace AgroVisionAI.Services
{
    public interface IAgroVisionApiClient
    {
        Task<CropPredictionResponse> PredictAsync(
            string imagePath,
            string originalFileName,
            CancellationToken cancellationToken = default);

        Task<AiHealthResponse> GetHealthAsync(
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AiManagedModelStatus>> GetManagedModelsAsync(
            CancellationToken cancellationToken = default);

        Task<AiManagedModelStatus> ActivateManagedModelAsync(
            string crop,
            string filename,
            CancellationToken cancellationToken = default);
    }
}
