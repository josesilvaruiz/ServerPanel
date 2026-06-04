using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ServerPanel.Pages;

public class SteamCallbackModel : PageModel
{
    private readonly IConfiguration _config;

    public SteamCallbackModel(IConfiguration config) => _config = config;

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await HttpContext.AuthenticateAsync("External");
        if (!result.Succeeded)
            return Redirect("/Login?error=steam");

        // Steam devuelve el SteamID64 en el claim NameIdentifier
        var steamId = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        // El formato es: https://steamcommunity.com/openid/id/76561198XXXXXXXXX
        if (steamId?.Contains('/') == true)
            steamId = steamId.Split('/').Last();

        var allowed = _config.GetSection("Steam:AllowedSteamIds").Get<string[]>() ?? [];

        await HttpContext.SignOutAsync("External");

        if (string.IsNullOrEmpty(steamId) || (allowed.Length > 0 && !allowed.Contains(steamId)))
            return Redirect("/Login?error=unauthorized");

        var name = result.Principal?.FindFirstValue(ClaimTypes.Name) ?? steamId;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name),
            new("SteamId", steamId)
        };
        var identity = new ClaimsIdentity(claims, "ServerPanel");
        await HttpContext.SignInAsync("ServerPanel", new ClaimsPrincipal(identity));

        return Redirect("/panel");
    }
}
