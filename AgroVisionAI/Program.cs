using System.Text;
using AgroVisionAI.Data;
using AgroVisionAI.Models;
using AgroVisionAI.Models.Api;
using AgroVisionAI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Private workstation settings are never published. Deployment values still win.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

// AgroVisionAI is an academic/individual project; configure QuestPDF once at startup.
// Re-check the QuestPDF license if this application is later used by an ineligible organization.
QuestPDF.Settings.License = LicenseType.Community;
QuestPDF.Settings.UseEnvironmentFonts = false;

builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultSQLConnection")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Keep the existing Identity cookie for MVC pages and add a separate Bearer
// scheme for mobile/REST clients. API controllers explicitly require ApiUser.
var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be configured with at least 32 characters. " +
        "Use the ignored appsettings.Local.json locally or the Jwt__Key environment variable in production.");
}

if (!builder.Environment.IsDevelopment() &&
    jwtOptions.Key.StartsWith("DEV_ONLY_", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The development JWT key cannot be used in Production. Configure Jwt__Key with a strong secret.");
}

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services
    .AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiUser", policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IDiagnosticReportService, DiagnosticReportService>();
builder.Services.AddScoped<IdentityDataSeeder>();

builder.Services.AddHttpClient<IAgroVisionApiClient, AgroVisionApiClient>(client =>
{
    var baseUrl = builder.Configuration["AgroVisionApi:BaseUrl"]
        ?? "http://127.0.0.1:8000/";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(120);

    var adminKey = builder.Configuration["AgroVisionApi:AdminKey"];
    if (!string.IsNullOrWhiteSpace(adminKey))
    {
        client.DefaultRequestHeaders.Add("X-AgroVision-Admin-Key", adminKey);
    }
});

builder.Services.AddHttpClient<IWeatherService, OpenMeteoWeatherService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AgroVisionAI/1.0");
});
builder.Services.AddSingleton<IDiseaseRiskService, DiseaseRiskService>();

var app = builder.Build();

// Docker/local deployment can opt in to automatic migrations through configuration.
// Visual Studio development keeps the existing explicit Update-Database workflow because
// Database:ApplyMigrationsOnStartup is false/absent by default.
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    const int maxAttempts = 12;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning(
                ex,
                "Database migration attempt {Attempt}/{MaxAttempts} failed. Retrying in 5 seconds.",
                attempt,
                maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

// Ensure the standard Identity roles exist and, when enabled, seed a local/demo administrator.
// Production can keep this disabled or provide values through environment variables.
using (var scope = app.Services.CreateScope())
{
    var identitySeeder = scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
    await identitySeeder.SeedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Lightweight liveness endpoint used by Docker and deployment platforms.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    AllowCachingResponses = false
});

app.MapStaticAssets();
app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
