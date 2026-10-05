using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace ServerPanel.Pages;

public partial class SteamCallbackModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly ILogger<SteamCallbackModel> _logger;

    public SteamCallbackModel(IConfiguration config, ILogger<SteamCallbackModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    // Steam devuelve el SteamID64 en el claim NameIdentifier con el formato
    // https://steamcommunity.com/openid/id/76561198XXXXXXXXX — se exige exactamente eso (el
    // handler de Steam acepta también http://, así que aquí igual).
    [GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/(\d{17})$")]
    private static partial Regex SteamClaimedId();

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await HttpContext.AuthenticateAsync("External");
        await HttpContext.SignOutAsync("External");
        if (!result.Succeeded)
            return Redirect("/Login?error=steam");

        var claimedId = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var match = SteamClaimedId().Match(claimedId);
        if (!match.Success)
            return Redirect("/Login?error=steam");
        var steamId = match.Groups[1].Value;

        var allowed = (_config.GetSection("Steam:AllowedSteamIds").Get<string[]>() ?? [])
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        // Lista vacía o sin configurar = nadie entra por Steam (igual que AllowedEmails con
        // Google). Antes una lista vacía dejaba entrar a cualquier cuenta de Steam.
        if (allowed.Count == 0)
        {
            _logger.LogWarning("Login con Steam rechazado: Steam:AllowedSteamIds está vacío o sin configurar");
            return Redirect("/Login?error=unauthorized");
        }
        if (!allowed.Contains(steamId))
        {
            _logger.LogWarning("Login con Steam rechazado para SteamID {SteamId}: no está en Steam:AllowedSteamIds", steamId);
            return Redirect("/Login?error=unauthorized");
        }

        // La identidad es el SteamID (fijo), no el nombre visible de Steam: ese lo puede cambiar
        // cualquiera en cualquier momento, incluso para que coincida con el de otro usuario.
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "steam:" + steamId),
            new("SteamId", steamId),
        };
        if (result.Principal?.FindFirstValue(ClaimTypes.Name) is { Length: > 0 } displayName)
            claims.Add(new Claim("SteamName", displayName));

        var identity = new ClaimsIdentity(claims, "ServerPanel");
        await HttpContext.SignInAsync("ServerPanel", new ClaimsPrincipal(identity));

        return LocalRedirect("~/panel");
    }
}
