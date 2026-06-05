using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using ServerPanel.Components;
using ServerPanel.Contracts;
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

        options.Events.OnSigningIn = context =>
        {
            Console.WriteLine("=== SERVERPANEL COOKIE SIGNING IN ===");
            Console.WriteLine($"Path: {context.Options.Cookie.Path}");
            Console.WriteLine($"Domain: {context.Options.Cookie.Domain}");
            Console.WriteLine($"SameSite: {context.Options.Cookie.SameSite}");
            Console.WriteLine($"SecurePolicy: {context.Options.Cookie.SecurePolicy}");
            Console.WriteLine("====================================");
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

var app = builder.Build();

// VERY IMPORTANT: before authentication and HTTPS handling
app.UseForwardedHeaders();
app.UsePathBase("/panel");
app.UseRouting();

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

app.Use(async (ctx, next) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ctx.Request.Method} {ctx.Request.Path} | PathBase={ctx.Request.PathBase} | Auth={ctx.User.Identity?.IsAuthenticated} | User={ctx.User.Identity?.Name}");
    await next();
});

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.Value?.Contains("_blazor/negotiate") == true)
    {
        Console.WriteLine("========== NEGOTIATE ==========");
        Console.WriteLine($"Path: {ctx.Request.Path}");
        Console.WriteLine($"Method: {ctx.Request.Method}");
        Console.WriteLine("--- Headers ---");
        foreach (var h in ctx.Request.Headers)
            Console.WriteLine($"{h.Key}: {h.Value}");
        Console.WriteLine("--- Cookies ---");
        foreach (var c in ctx.Request.Cookies)
            Console.WriteLine($"{c.Key}={c.Value}");
        Console.WriteLine("===============================");
    }
    await next();
    if (ctx.Request.Path.Value?.Contains("_blazor/negotiate") == true)
        Console.WriteLine($"[NEGOTIATE POST] Status={ctx.Response.StatusCode}");
});

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapRazorPages();

// ── Diagnostic: listar todos los endpoints registrados ───────────────────
var endpointDataSources = app.Services.GetServices<EndpointDataSource>();
foreach (var source in endpointDataSources)
    foreach (var endpoint in source.Endpoints)
    {
        var route = endpoint as RouteEndpoint;
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        Console.WriteLine($"{endpoint.DisplayName} | Pattern={route?.RoutePattern.RawText} | Methods={string.Join(",", methods ?? [])}");
    }

app.Run();
