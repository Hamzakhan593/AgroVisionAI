using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AgroVisionAI.Models;
using AgroVisionAI.Models.Api;
using AgroVisionAI.Models.Api.Mobile;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgroVisionAI.Services
{
    public sealed class JwtTokenService : IJwtTokenService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly JwtOptions _options;

        public JwtTokenService(
            UserManager<ApplicationUser> userManager,
            IOptions<JwtOptions> options)
        {
            _userManager = userManager;
            _options = options.Value;
        }

        public async Task<AuthTokenResponse> CreateTokenAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(Math.Clamp(_options.AccessTokenMinutes, 5, 1440));

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(ClaimTypes.NameIdentifier, user.Id),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(ClaimTypes.Name, user.FullName),
                new(ClaimTypes.Email, user.Email ?? string.Empty)
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: credentials);

            return new AuthTokenResponse
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAtUtc = expires,
                User = new ApiUserResponse
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email ?? string.Empty,
                    Roles = roles.ToArray()
                }
            };
        }
    }
}
