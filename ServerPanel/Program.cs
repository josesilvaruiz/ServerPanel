using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using ServerPanel.Components;
using ServerPanel.Contracts;
using ServerPanel.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
        ForwardedHeaders.XForwardedProto;

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

var app = builder.Build();

// VERY IMPORTANT: before authentication and HTTPS handling
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();

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

app.MapRazorPages();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();