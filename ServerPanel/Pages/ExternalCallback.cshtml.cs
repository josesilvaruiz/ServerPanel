using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ServerPanel.Pages;

public class ExternalCallbackModel : PageModel
{
    private readonly IConfiguration _config;

    public ExternalCallbackModel(IConfiguration config) => _config = config;

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await HttpContext.AuthenticateAsync("External");
        if (!result.Succeeded)
            return Redirect("/Login?error=google");

        var email = result.Principal?.FindFirstValue(ClaimTypes.Email);
        var allowed = _config.GetSection("Auth:AllowedEmails").Get<string[]>() ?? [];

        await HttpContext.SignOutAsync("External");

        if (string.IsNullOrEmpty(email) || !allowed.Contains(email, StringComparer.OrdinalIgnoreCase))
            return Redirect("/Login?error=unauthorized");

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Email, email)
        };
        var identity = new ClaimsIdentity(claims, "ServerPanel");
        await HttpContext.SignInAsync("ServerPanel", new ClaimsPrincipal(identity));

        return LocalRedirect("~/panel");
    }
}
