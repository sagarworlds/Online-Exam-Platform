using System.Text;
using System.Threading.RateLimiting;
using ExamPlatform.Api;
using ExamPlatform.Api.RateLimiting;
using ExamPlatform.Api.WhatsApp;
using ExamPlatform.Modules.Admin.Endpoints;
using ExamPlatform.Modules.Batch.Endpoints;
using ExamPlatform.Modules.Consent.Endpoints;
using ExamPlatform.Modules.ExamAuthoring.Endpoints;
using ExamPlatform.Modules.ExamRuntime.Endpoints;
using ExamPlatform.Modules.Guardian.Endpoints;
using ExamPlatform.Modules.Identity.Endpoints;
using ExamPlatform.Modules.Invite.Endpoints;
using ExamPlatform.Modules.Proctoring.Endpoints;
using ExamPlatform.Modules.QuestionBank.Endpoints;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using ExamPlatform.SharedKernel.Infrastructure.Sms;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting services shared by every module (Clock, domain-event dispatch) —
// registered once here, before any module's AddModule, so every module resolves
// the same singleton Clock/dispatcher instead of each registering its own.
builder.Services.AddSharedKernel();

// Who is acting in the current request, so code that reacts to a domain event can name the actor in the audit trail.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddScoped<IClientInfo, HttpClientInfo>();
builder.Services.AddScoped<IRequestLanguage, HttpRequestLanguage>();

// One mail sender for everything the platform sends (invitations, OTP codes, answers to attempt requests). Mail:Provider
// picks the transport: Smtp (the default, unchanged behaviour) or BrevoApi, which sends over HTTPS for a host (e.g.
// Render's free plan) that blocks outbound SMTP ports. With neither configured nothing is sent, and each caller says
// so to the person who needs to pass the message on by hand.
var mailProvider = builder.Configuration.GetSection(MailOptions.SectionName).Get<MailOptions>()?.Provider ?? MailOptions.Smtp;
if (mailProvider == MailOptions.BrevoApi)
{
    builder.Services.AddOptions<BrevoOptions>().Bind(builder.Configuration.GetSection(BrevoOptions.SectionName));
    builder.Services.AddBrevoApiMailer();
}
else
{
    builder.Services.AddOptions<SmtpOptions>().Bind(builder.Configuration.GetSection(SmtpOptions.SectionName));
    builder.Services.AddSmtpMailer();
}

// WhatsApp (Meta's Cloud API): one sender for whatever the platform sends there (sign-in codes now). It sends nothing, and says
// so, until the WhatsApp section is filled in; Identity:OtpDelivery:PhoneProvider decides whether phone codes use it.
builder.Services.AddOptions<WhatsAppOptions>().Bind(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.AddWhatsAppCloudApi();

// SMS: one sender behind the Sms:Enabled master switch, off unless set to true. No SMS provider is built in yet, so nothing is
// sent even when the switch is on, and each attempt is logged. Render's setting is documented in render.yaml.
builder.Services.AddOptions<SmsOptions>().Bind(builder.Configuration.GetSection(SmsOptions.SectionName));
builder.Services.AddSmsSender();

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
            ClientRateLimitPartition.FixedWindow(
                httpContext, limits.PermitLimit, TimeSpan.FromSeconds(limits.WindowSeconds)));
    });

// Trust X-Forwarded-For/-Proto only from the proxies named in ForwardedHeaders:*, so the
// per-IP limiters below see the real client behind a load balancer (NFR-5).
builder.Services.AddSingleton<IConfigureOptions<ForwardedHeadersOptions>, ForwardedHeadersOptionsSetup>();

// Enums as JSON strings everywhere (e.g. "PrivacyNotice"), not their numeric values —
// matches how query-string enum binding already works, so the API is consistent
// whether a value arrives via a route/query parameter or a JSON request body.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Compressed responses for a candidate on a slow connection (FR-53): an exam's questions are the largest thing the API sends, and text
// compresses well. Brotli where the browser asks for it, gzip otherwise. TLS ends at the proxy, so a response is compressed whatever
// the scheme the request claims to have used (EnableForHttps), except for the sign-in routes (see below).
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
});

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
    new QuestionBankModuleInstaller(),
    new ExamRuntimeModuleInstaller(),
    new ProctoringModuleInstaller(),
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

// The API description and its browsable UI are a development aid, not part of the deployed
// surface: they would hand an attacker a map of every route. One flag both maps them below
// and exempts the UI from the Content-Security-Policy, so the two cannot drift apart.
var apiReferenceEnabled = app.Environment.IsDevelopment();

// Outside everything that writes a body, so each response is compressed on its way out. Not for the sign-in routes: their answers carry
// tokens, and compressing a secret next to text a caller can influence is what the BREACH attack on compressed HTTPS needs. They are
// small, so nothing is lost.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/v1/auth", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseResponseCompression());

// Before the exception handler, so error responses carry the headers too.
app.UseMiddleware<SecurityHeadersMiddleware>(apiReferenceEnabled);
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (apiReferenceEnabled)
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/v1/health");
app.MapWhatsAppWebhook();

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

// Schema and reference data (roles, permissions, notice versions) are applied by each module's
// own idempotent MigrateAndSeedAsync, never by EF migration bundles, which would apply the
// schema but skip the seeders and leave a database where registration fails (ADR 0002).
// There are two ways in:
//  - `ExamPlatform.Api.dll --migrate-and-seed` runs every module once and exits without
//    starting the web host. A deployment runs it as ONE pre-deploy job or init container, so
//    two replicas never race to insert the same role or permission.
//  - Database:MigrateAndSeedOnStartup=true does the same at the start of a normal run. It is
//    on in Development, so `dotnet run` gives a ready-to-use database, and off elsewhere.
// Routed through each module's own MigrateAndSeedAsync rather than the Host resolving
// a DbContext directly, so the Host never references a module's Infrastructure project.
var migrateAndSeedOnly = args.Contains("--migrate-and-seed", StringComparer.Ordinal);
var migrateAndSeedOnStartup = app.Configuration.GetValue(
    "Database:MigrateAndSeedOnStartup", defaultValue: app.Environment.IsDevelopment());
if (migrateAndSeedOnly || migrateAndSeedOnStartup)
{
    using var scope = app.Services.CreateScope();
    foreach (var module in modules)
    {
        await module.MigrateAndSeedAsync(scope.ServiceProvider, CancellationToken.None);
        app.Logger.LogInformation("Migrated and seeded {ModuleName}", module.ModuleName);
    }
}

// Returns before app.Run(): the web host is never started, so a deployment job ends as soon as
// the database is ready, and the startup checks that belong to a serving host (such as the
// OTP delivery options' ValidateOnStart) are not run by it.
if (migrateAndSeedOnly)
{
    return;
}

app.Run();

/// <summary>Composition root entry point, exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;
