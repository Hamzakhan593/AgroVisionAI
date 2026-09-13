using AgroVisionAI.Models;
using AgroVisionAI.Models.Api.Mobile;

namespace AgroVisionAI.Services
{
    public interface IJwtTokenService
    {
        Task<AuthTokenResponse> CreateTokenAsync(ApplicationUser user);
    }
}
