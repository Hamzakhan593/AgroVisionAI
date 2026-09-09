namespace AgroVisionAI.Services
{
    public sealed class AgroVisionApiException : Exception
    {
        public AgroVisionApiException(string message, int? statusCode = null, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
        }

        public int? StatusCode { get; }
    }
}

