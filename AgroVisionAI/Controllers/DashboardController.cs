using AgroVisionAI.Data;
using AgroVisionAI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroVisionAI.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var recentDetections = await _context.Detections
                .Where(d => d.UserId == user.Id)
                .OrderByDescending(d => d.CreatedAt)
                .Take(3)
                .ToListAsync();

            var model = new DashboardViewModel
            {
                User = user,
                RecentDetections = recentDetections
            };

            return View(model);
        }
    }
}