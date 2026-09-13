namespace AgroVisionAI.Models.Api
{
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = "AgroVisionAI";
        public string Audience { get; set; } = "AgroVisionAI.Mobile";
        public string Key { get; set; } = string.Empty;
        public int AccessTokenMinutes { get; set; } = 60;
    }
}
