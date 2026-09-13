using AgroVisionAI.Data;
using AgroVisionAI.Models;
using AgroVisionAI.Models.Api.Mobile;
using AgroVisionAI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AgroVisionAI.Controllers.Api
{
    [ApiController]
    [Route("api/mobile/auth")]
    public sealed class AuthApiController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IJwtTokenService _tokenService;

        public AuthApiController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IJwtTokenService tokenService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _tokenService = tokenService;
        }

        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<ActionResult<AuthTokenResponse>> Register(
            [FromBody] ApiRegisterRequest request)
        {
            var email = request.Email.Trim();
            var fullName = request.FullName.Trim();

            if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Passwords do not match." });
            }

            if (await _userManager.FindByEmailAsync(email) != null)
            {
                return Conflict(new { message = "An account with this email already exists." });
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    message = "Registration failed.",
                    errors = result.Errors.Select(item => item.Description).ToArray()
                });
            }

            var roleResult = await _userManager.AddToRoleAsync(user, IdentityDataSeeder.UserRole);
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                return BadRequest(new
                {
                    message = "Registration failed while assigning the default user role.",
                    errors = roleResult.Errors.Select(item => item.Description).ToArray()
                });
            }

            return Ok(await _tokenService.CreateTokenAsync(user));
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<AuthTokenResponse>> Login(
            [FromBody] ApiLoginRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email.Trim());
            if (user == null)
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var result = await _signInManager.CheckPasswordSignInAsync(
                user,
                request.Password,
                lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    message = "Your account is temporarily locked. Please try again later."
                });
            }

            if (!result.Succeeded)
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            return Ok(await _tokenService.CreateTokenAsync(user));
        }
    }
}
