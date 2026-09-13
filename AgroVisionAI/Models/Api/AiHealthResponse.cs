namespace AgroVisionAI.Models.Api
{
    public sealed class AiHealthResponse
    {
        public string Status { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public List<AiModelStatus> Models { get; set; } = new();
    }

    public sealed class AiModelStatus
    {
        public string Crop { get; set; } = string.Empty;
        public bool Available { get; set; }
        public bool Loaded { get; set; }
        public string? Filename { get; set; }
        public List<string> Classes { get; set; } = new();
        public string? Preprocessing { get; set; }
        public string? Error { get; set; }
    }
}
