using System.Text;
using System.Threading.RateLimiting;
using ExamPlatform.Api;
using ExamPlatform.Modules.Admin.Endpoints;
using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.Modules.Consent.Endpoints;
using ExamPlatform.Modules.Consent.Infrastructure;
using ExamPlatform.Modules.Identity.Endpoints;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting services shared by every module (Clock, domain-event dispatch) —
// registered once here, before any module's AddModule, so every module resolves
// the same singleton Clock/dispatcher instead of each registering its own.
builder.Services.AddSharedKernel();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Configuration value 'Jwt:SigningKey' is required.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep raw JWT claim names ("sub", "perm", "sid") instead of ASP.NET Core's
        // default remapping to long ClaimTypes URIs — every module reads claims by
        // their literal JWT name (see Identity.Infrastructure's JwtTokenGenerator).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:4200"];
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddRateLimiter(options =>
{
    // A single global, per-IP limiter for this slice (NFR-5: rate limiting). Scoping a
    // stricter limit specifically to OTP-request endpoints is a follow-up once real
    // traffic patterns exist — see ADR 0001's note on why Redis isn't wired in yet.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

// Modules register themselves here; adding a future module is one array entry and
// nothing else changes in this file (Open/Closed Principle — see ADR 0001).
IModuleInstaller[] modules =
[
    new IdentityModuleInstaller(),
    new ConsentModuleInstaller(),
    new AdminModuleInstaller(),
];

foreach (var module in modules)
{
    module.AddModule(builder.Services, builder.Configuration);
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/v1/health");

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

// Development-only convenience: migrate and seed every module's schema on startup so
// `dotnet run` gives a ready-to-use database without a separate migration step. A real
// deployment pipeline runs migrations explicitly instead (see README quickstart).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await identityDb.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(identityDb, CancellationToken.None);

    var consentDb = scope.ServiceProvider.GetRequiredService<ConsentDbContext>();
    await consentDb.Database.MigrateAsync();
    var clock = scope.ServiceProvider.GetRequiredService<Clock>();
    await ConsentSeeder.SeedAsync(consentDb, clock.UtcNow, CancellationToken.None);

    var adminDb = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    await adminDb.Database.MigrateAsync();
}

app.Run();

/// <summary>Composition root entry point, exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;
