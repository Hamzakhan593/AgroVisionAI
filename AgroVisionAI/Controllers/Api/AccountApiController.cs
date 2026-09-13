using AgroVisionAI.Models;
using AgroVisionAI.Models.Api.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AgroVisionAI.Controllers.Api
{
    [ApiController]
    [Route("api/mobile/account")]
    [Authorize(Policy = "ApiUser")]
    public sealed class AccountApiController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public AccountApiController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [HttpGet("me")]
        public async Task<ActionResult<ApiUserResponse>> Me()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            var roles = await _userManager.GetRolesAsync(user);
            return Ok(new ApiUserResponse
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Roles = roles.ToArray()
            });
        }
    }
}
