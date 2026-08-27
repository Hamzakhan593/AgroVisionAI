using AgroVisionAI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AgroVisionAI.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public ProfileController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(
        string fullName,
        string email)
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                ModelState.AddModelError(
                    "FullName",
                    "Full name is required.");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                ModelState.AddModelError(
                    "Email",
                    "Email address is required.");
            }

            if (!ModelState.IsValid)
            {
                return View(user);
            }

            var normalizedEmail = email.Trim();

            var existingUser =
                await _userManager.FindByEmailAsync(normalizedEmail);

            if (existingUser != null &&
                existingUser.Id != user.Id)
            {
                ModelState.AddModelError(
                    "Email",
                    "This email address is already in use.");

                return View(user);
            }

            user.FullName = fullName.Trim();

            var result = await _userManager.SetEmailAsync(
                user,
                normalizedEmail);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(
                        "",
                        error.Description);
                }

                return View(user);
            }

            result = await _userManager.SetUserNameAsync(
                user,
                normalizedEmail);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(
                        "",
                        error.Description);
                }

                return View(user);
            }

            result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(
                        "",
                        error.Description);
                }

                return View(user);
            }

            await _signInManager.RefreshSignInAsync(user);

            TempData["ProfileSuccess"] =
                "Your profile has been updated successfully.";

            return RedirectToAction(nameof(Index));
        }
    }
}