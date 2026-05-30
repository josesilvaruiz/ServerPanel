using ServerPanel.Components;
using ServerPanel.Contracts;
using ServerPanel.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Enable Razor Pages so the /Login Razor Page can be served
builder.Services.AddRazorPages();

builder.Services.AddSingleton<IServerQueryService, ServerQueryService>();
builder.Services.AddSingleton<ISshService, SshService>();
builder.Services.AddSingleton<ICs2ServerService, Cs2ServerService>();
builder.Services.AddScoped<IPlayerService, PlayerService>();

builder.Services
    .AddAuthentication("ServerPanel")
    .AddCookie("ServerPanel", options =>
    {
        // Use the Razor Page /Login as the login path
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/";
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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorPages();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
