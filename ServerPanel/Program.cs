using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServerPanel.Components;
using ServerPanel.Contracts;
using ServerPanel.Data;
using ServerPanel.Hubs;
using ServerPanel.Models;
using ServerPanel.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();

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
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")), ServiceLifetime.Scoped);

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
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = 401;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
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

var analyticsApiKey = builder.Configuration["Analytics:ApiKey"] ?? "";

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

// Seed after app starts so PostgresTunnelService has time to open the tunnel
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(async () =>
{
    await Task.Delay(3000); // give PostgresTunnelService time to open tunnel
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    for (int attempt = 1; attempt <= 5; attempt++)
    {
        try
        {
            db.Database.Migrate();
            await DbSeeder.SeedSharedCommandsAsync(db);
            break;
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
            logger.LogWarning("BD no disponible (intento {N}/5): {Msg}", attempt, ex.Message);
            if (attempt < 5) await Task.Delay(3000);
        }
    }
});

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

app.MapHub<TerminalHub>("/hubs/terminal")
   .DisableAntiforgery()
   .RequireAuthorization();

app.MapGet("/api/file/download", async (string path, ISshService ssh, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest("path requerido");
    var stream   = await ssh.DownloadFileAsync(path);
    var fileName = System.IO.Path.GetFileName(path);
    return Results.Stream(stream, "application/octet-stream", fileName);
})
.RequireAuthorization()
.DisableAntiforgery();

app.MapPost("/api/analytics/visit", async (
    HttpContext ctx,
    ApplicationDbContext db,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("Analytics");
    var origin = ctx.Request.Headers.Origin.FirstOrDefault() ?? "";
    var ip     = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (!allowedOrigins.Contains(origin))
    {
        logger.LogWarning("Analytics origen no permitido — Origin:{Origin} IP:{IP}", origin, ip);
        return Results.Json(new { error = "Forbidden" }, statusCode: 403);
    }

    var key = ctx.Request.Headers["X-Analytics-Key"].FirstOrDefault() ?? "";
    logger.LogWarning(
        "Analytics Auth Debug | ReceivedKey:{ReceivedKey} | ConfiguredKey:{ConfiguredKey} | Origin:{Origin}",
        key, analyticsApiKey, origin);
    if (analyticsApiKey.Length == 0 || key != analyticsApiKey)
    {
        logger.LogWarning("Analytics API key inválida — Origin:{Origin} IP:{IP}", origin, ip);
        return Results.Json(new { error = "Unauthorized" }, statusCode: 401);
    }

    PageVisitRequest? req;
    try
    {
        req = await System.Text.Json.JsonSerializer.DeserializeAsync<PageVisitRequest>(
            ctx.Request.Body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            ct);
    }
    catch
    {
        return Results.BadRequest("Body JSON inválido");
    }

    if (req is null) return Results.BadRequest("Body vacío");

    try
    {
        var visit = new PageVisit
        {
            TimestampUtc      = DateTime.UtcNow,
            Path              = req.Path,
            Referrer          = ctx.Request.Headers.Referer.FirstOrDefault(),
            UserAgent         = ctx.Request.Headers.UserAgent.FirstOrDefault(),
            Language          = req.Language,
            ScreenWidth       = req.ScreenWidth,
            ScreenHeight      = req.ScreenHeight,
            TimeOnPageSeconds = req.TimeOnPageSeconds,
        };

        db.PageVisits.Add(visit);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Analytics visita guardada — Id:{Id} Path:{Path} Origin:{Origin}", visit.Id, visit.Path, origin);
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
.DisableAntiforgery()
.AllowAnonymous()
.WithName("TrackVisit"); // AllowAnonymous necesario — auth es por API key, no por cookie

app.Run();
