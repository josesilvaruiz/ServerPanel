using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServerPanel.Components;
using ServerPanel.Contracts;
using ServerPanel.Data;
using ServerPanel.Models;
using ServerPanel.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAntiforgery();

// Enable Razor Pages so the /Login Razor Page can be served
builder.Services.AddRazorPages();

builder.Services.AddLocalization();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IServerQueryService, ServerQueryService>();
builder.Services.AddSingleton<ISshService, SshService>();
builder.Services.AddSingleton<ICs2ServerService, Cs2ServerService>();
builder.Services.AddSingleton<IServerMetricsService, ServerMetricsService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHostedService<MetricsCollectorBackgroundService>();
builder.Services.AddHostedService<Cs2MetricsCollectorBackgroundService>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<PostgresTunnelService>();
builder.Services.AddScoped<IPlayerService, PlayerService>();
builder.Services.AddScoped<ServerPanel.Services.ThemeState>();

// Support reverse proxies such as Nginx
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services
    .AddAuthentication("ServerPanel")
    .AddCookie("ServerPanel", options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
    })
    .AddCookie("External", options =>
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
        options.SignInScheme = "External";
        options.CallbackPath = "/signin-google";
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddSteam(options =>
    {
        options.ApplicationKey = builder.Configuration["Steam:ApiKey"]!;
        options.SignInScheme = "External";
        options.CallbackPath = "/signin-steam";
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

var allowedOrigins = builder.Configuration
    .GetSection("Analytics:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Analytics", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("analytics", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 30,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            }));

    options.OnRejected = (ctx, _) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var logger = ctx.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Analytics");
        logger.LogWarning(
            "Analytics rate limit exceeded — IP:{IP}",
            ctx.HttpContext.Connection.RemoteIpAddress);
        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

// VERY IMPORTANT: before authentication and HTTPS handling
app.UseForwardedHeaders();
app.UsePathBase("/panel");
app.UseRouting();
app.UseCors("Analytics");
app.UseRateLimiter();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

var supportedCultures = new[] { "es", "en" };

app.UseRequestLocalization(
    new RequestLocalizationOptions()
        .SetDefaultCulture("es")
        .AddSupportedCultures(supportedCultures)
        .AddSupportedUICultures(supportedCultures));

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapRazorPages();

app.MapPost("/api/analytics/visit", async (
    PageVisitRequest req,
    HttpContext ctx,
    ApplicationDbContext db,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("Analytics");
    var origin    = ctx.Request.Headers.Origin.FirstOrDefault() ?? "";
    var ip        = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var method    = ctx.Request.Method;
    var path      = ctx.Request.Path;
    var body      = req.Path;

    logger.LogInformation(
        "Analytics POST recibido — Method:{Method} Path:{Path} Origin:{Origin} IP:{IP} Body.Path:{BodyPath}",
        method, path, origin, ip, body);

    logger.LogInformation("Analytics Origin (sin validar) — guardando visita Path:{BodyPath}", body);

    try
    {
        var visit = new PageVisit
        {
            TimestampUtc = DateTime.UtcNow,
            Path         = body,
            Referrer     = ctx.Request.Headers.Referer.FirstOrDefault(),
            UserAgent    = ctx.Request.Headers.UserAgent.FirstOrDefault(),
        };

        db.PageVisits.Add(visit);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Analytics visita guardada — Id:{Id} Path:{Path}", visit.Id, visit.Path);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Analytics error al guardar visita");
        throw;
    }
})
.RequireCors("Analytics")
.RequireRateLimiting("analytics")
.WithName("TrackVisit");

app.Run();
