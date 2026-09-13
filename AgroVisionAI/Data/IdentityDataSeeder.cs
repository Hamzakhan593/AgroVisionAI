using AgroVisionAI.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgroVisionAI.Data
{
    public sealed class IdentityDataSeeder
    {
        public const string AdminRole = "Admin";
        public const string UserRole = "User";

        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IdentityDataSeeder> _logger;

        public IdentityDataSeeder(
            RoleManager<IdentityRole> roleManager,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            ILogger<IdentityDataSeeder> logger)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            await EnsureRoleAsync(AdminRole);
            await EnsureRoleAsync(UserRole);
            await AssignDefaultRoleToExistingUsersAsync(cancellationToken);

            if (!_configuration.GetValue<bool>("IdentitySeed:Enabled"))
            {
                _logger.LogInformation("Default admin seeding is disabled.");
                return;
            }

            var email = (_configuration["IdentitySeed:Email"] ?? string.Empty).Trim();
            var fullName = (_configuration["IdentitySeed:FullName"] ?? "AgroVision Admin").Trim();
            var password = _configuration["IdentitySeed:Password"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning(
                    "IdentitySeed is enabled, but admin email/password is missing. Admin account was not created.");
                return;
            }

            var admin = await _userManager.FindByEmailAsync(email);
            if (admin == null)
            {
                admin = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? "AgroVision Admin" : fullName
                };

                var createResult = await _userManager.CreateAsync(admin, password);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join(" | ", createResult.Errors.Select(error => error.Description));
                    throw new InvalidOperationException($"Default admin account could not be created: {errors}");
                }
            }
            else if (string.IsNullOrWhiteSpace(admin.FullName) && !string.IsNullOrWhiteSpace(fullName))
            {
                admin.FullName = fullName;
                await _userManager.UpdateAsync(admin);
            }

            if (!await _userManager.IsInRoleAsync(admin, AdminRole))
            {
                var roleResult = await _userManager.AddToRoleAsync(admin, AdminRole);
                if (!roleResult.Succeeded)
                {
                    var errors = string.Join(" | ", roleResult.Errors.Select(error => error.Description));
                    throw new InvalidOperationException($"Admin role could not be assigned: {errors}");
                }
            }

            if (await _userManager.IsInRoleAsync(admin, UserRole))
            {
                await _userManager.RemoveFromRoleAsync(admin, UserRole);
            }

            _logger.LogInformation("Default AgroVision administrator is ready: {AdminEmail}", email);
        }

        private async Task EnsureRoleAsync(string roleName)
        {
            if (await _roleManager.RoleExistsAsync(roleName))
            {
                return;
            }

            var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                var errors = string.Join(" | ", result.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Identity role '{roleName}' could not be created: {errors}");
            }
        }

        private async Task AssignDefaultRoleToExistingUsersAsync(CancellationToken cancellationToken)
        {
            var users = await _userManager.Users.ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Count == 0)
                {
                    var result = await _userManager.AddToRoleAsync(user, UserRole);
                    if (!result.Succeeded)
                    {
                        _logger.LogWarning(
                            "Could not assign the default User role to account {Email}.",
                            user.Email ?? user.Id);
                    }
                }
            }
        }
    }
}
