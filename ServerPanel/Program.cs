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

builder.Services
    .AddAuthentication("ServerPanel")
    .AddCookie("ServerPanel", options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
    })
    .AddCookie("External")
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
        options.SignInScheme = "External";
        options.CallbackPath = "/signin-google";
    })
    .AddSteam(options =>
    {
        options.ApplicationKey = builder.Configuration["Steam:ApiKey"]!;
        options.SignInScheme = "External";
        options.CallbackPath = "/signin-steam";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

var supportedCultures = new[] { "es", "en" };
app.UseRequestLocalization(new RequestLocalizationOptions()
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
