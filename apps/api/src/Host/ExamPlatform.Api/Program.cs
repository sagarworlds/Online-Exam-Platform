using System.Text;
using System.Threading.RateLimiting;
using ExamPlatform.Api;
using ExamPlatform.Api.RateLimiting;
using ExamPlatform.Modules.Admin.Endpoints;
using ExamPlatform.Modules.Batch.Endpoints;
using ExamPlatform.Modules.Consent.Endpoints;
using ExamPlatform.Modules.ExamAuthoring.Endpoints;
using ExamPlatform.Modules.Guardian.Endpoints;
using ExamPlatform.Modules.Identity.Endpoints;
using ExamPlatform.Modules.Invite.Endpoints;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting services shared by every module (Clock, domain-event dispatch) —
// registered once here, before any module's AddModule, so every module resolves
// the same singleton Clock/dispatcher instead of each registering its own.
builder.Services.AddSharedKernel();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep raw JWT claim names ("sub", "perm", "sid") instead of ASP.NET Core's
        // default remapping to long ClaimTypes URIs — every module reads claims by
        // their literal JWT name (see Identity.Infrastructure's JwtTokenGenerator).
        options.MapInboundClaims = false;

        // Read the signing key here, inside the options delegate, rather than into a
        // variable above: JwtBearerOptions is materialized lazily (on first request,
        // after the host has fully built), so a value captured earlier — e.g. before a
        // test's WebApplicationFactory finishes layering its configuration overrides on
        // top of builder.Configuration — can go stale and silently sign with one key
        // while this validates against another.
        var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Configuration value 'Jwt:SigningKey' is required.");
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

        // Events are deliberately not set here: the Identity module attaches its own (see
        // IdentityModuleInstaller), which checks every validated token's "sid" against its
        // stored session (FR-4), since only Identity knows what a session is.
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:4200"];
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

// A global, per-IP fixed-window limiter (NFR-5: rate limiting), in-process for now — see
// ADR 0001's note on why Redis isn't wired in yet. Its limits come from configuration
// and are checked at startup, so a zero or negative value fails the boot instead of
// surfacing as a 500 on the first request.
builder.Services.AddOptions<GlobalRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(GlobalRateLimitOptions.SectionName))
    .Validate(
        limits => limits.PermitLimit > 0 && limits.WindowSeconds > 0,
        $"{GlobalRateLimitOptions.SectionName}:PermitLimit and :WindowSeconds must both be positive.")
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = RateLimitRejectionWriter.WriteProblemDetailsAsync;
});

// The limiter reads its limits through IOptions<GlobalRateLimitOptions> when
// RateLimiterOptions is first materialized, not from values captured here: the same
// staleness concern as the JWT signing key above, since a test's WebApplicationFactory
// layers its configuration overrides (e.g. a raised limit) on after this line runs.
builder.Services.AddOptions<RateLimiterOptions>()
    .Configure<IOptions<GlobalRateLimitOptions>>((options, globalLimits) =>
    {
        var limits = globalLimits.Value;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PermitLimit,
                    Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                }));
    });

// Trust X-Forwarded-For/-Proto only from the proxies named in ForwardedHeaders:*, so the
// per-IP limiters below see the real client behind a load balancer (NFR-5).
builder.Services.AddSingleton<IConfigureOptions<ForwardedHeadersOptions>, ForwardedHeadersOptionsSetup>();

// Enums as JSON strings everywhere (e.g. "PrivacyNotice"), not their numeric values —
// matches how query-string enum binding already works, so the API is consistent
// whether a value arrives via a route/query parameter or a JSON request body.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddProblemDetails();

// Modules register themselves here; adding a future module is one array entry and
// nothing else changes in this file (Open/Closed Principle — see ADR 0001).
IModuleInstaller[] modules =
[
    new IdentityModuleInstaller(),
    new ConsentModuleInstaller(),
    new AdminModuleInstaller(),
    new ExamAuthoringModuleInstaller(),
    new BatchModuleInstaller(),
    new InviteModuleInstaller(),
    new GuardianModuleInstaller(),
];

foreach (var module in modules)
{
    module.AddModule(builder.Services, builder.Configuration);
}

var app = builder.Build();

// First, so everything after it (rate limiting, logging, HSTS) sees the forwarded client
// address and scheme. Without it every client behind a proxy shares one rate-limit
// partition, and a header from an untrusted sender must never choose its own partition.
app.UseForwardedHeaders();

// HSTS is for browsers talking to a deployed host over HTTPS; in Development it would
// pin localhost to HTTPS. TLS ends at the proxy, so there is no UseHttpsRedirection.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Before the exception handler, so error responses carry the headers too.
app.UseMiddleware<SecurityHeadersMiddleware>(app.Environment.IsDevelopment());
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// The API description and its browsable UI are a development aid, not part of the
// deployed surface: they would hand an attacker a map of every route.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/v1/health");

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

// Development-only convenience: migrate and seed every module's schema on startup so
// `dotnet run` gives a ready-to-use database without a separate migration step. A real
// deployment pipeline runs migrations explicitly instead (see README quickstart).
// Routed through each module's own MigrateAndSeedAsync rather than the Host resolving
// a DbContext directly, so the Host never references a module's Infrastructure project.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    foreach (var module in modules)
    {
        await module.MigrateAndSeedAsync(scope.ServiceProvider, CancellationToken.None);
    }
}

app.Run();

/// <summary>Composition root entry point, exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;
