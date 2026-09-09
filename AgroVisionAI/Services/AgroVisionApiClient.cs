using System.Net.Http.Headers;
using System.Text.Json;
using AgroVisionAI.Models.Api;

namespace AgroVisionAI.Services
{
    public sealed class AgroVisionApiClient : IAgroVisionApiClient
    {
        private readonly HttpClient _httpClient;

        public AgroVisionApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<CropPredictionResponse> PredictAsync(
            string imagePath,
            string originalFileName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await using var imageStream = new FileStream(
                    imagePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);

                using var form = new MultipartFormDataContent();
                using var imageContent = new StreamContent(imageStream);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(imagePath));
                form.Add(imageContent, "file", Path.GetFileName(originalFileName));

                using var response = await _httpClient.PostAsync(
                    "api/v1/predict",
                    form,
                    cancellationToken);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new AgroVisionApiException(
                        ExtractError(body, response.ReasonPhrase),
                        (int)response.StatusCode);
                }

                var prediction = JsonSerializer.Deserialize<CropPredictionResponse>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (prediction == null || string.IsNullOrWhiteSpace(prediction.Disease))
                {
                    throw new AgroVisionApiException("The AI service returned an invalid response.");
                }

                if (prediction.CropConfidence is not double cropConfidence ||
                    !double.IsFinite(cropConfidence) || cropConfidence < 0 || cropConfidence > 1 ||
                    string.IsNullOrWhiteSpace(prediction.CropModelName) ||
                    !double.IsFinite(prediction.Confidence) || prediction.Confidence < 0 || prediction.Confidence > 1)
                {
                    throw new AgroVisionApiException("The AI service did not return a valid crop-validation result. Update and restart AIService before trying again.");
                }

                return prediction;
            }
            catch (AgroVisionApiException)
            {
                throw;
            }
            catch (TaskCanceledException exc) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AgroVisionApiException(
                    "The AI service took too long to respond. Please try again.",
                    inner: exc);
            }
            catch (HttpRequestException exc)
            {
                throw new AgroVisionApiException(
                    "The AI service is not running. Start AIService\\start_api.bat and try again.",
                    inner: exc);
            }
            catch (JsonException exc)
            {
                throw new AgroVisionApiException(
                    "The AI service returned unreadable prediction data.",
                    inner: exc);
            }
        }

        private static string GetContentType(string imagePath)
        {
            return Path.GetExtension(imagePath).ToLowerInvariant() == ".png"
                ? "image/png"
                : "image/jpeg";
        }

        private static string ExtractError(string responseBody, string? fallback)
        {
            try
            {
                using var json = JsonDocument.Parse(responseBody);
                if (json.RootElement.TryGetProperty("detail", out var detail))
                {
                    if (detail.ValueKind == JsonValueKind.String)
                    {
                        return detail.GetString() ?? "AI analysis failed.";
                    }

                    return detail.GetRawText();
                }
            }
            catch (JsonException)
            {
                // Use the safe fallback below when an upstream error is not JSON.
            }

            return string.IsNullOrWhiteSpace(fallback)
                ? "AI analysis failed."
                : $"AI analysis failed: {fallback}.";
        }
    }
}
