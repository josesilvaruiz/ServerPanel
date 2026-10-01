using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ServerPanel.Pages;

// Login temporal de usuario/contraseña, solo para poder entrar por IP mientras esta
// copia del panel no tiene un dominio propio (Google OAuth no acepta redirect URIs
// sin dominio). Se quita en cuanto haya DNS.
public class LocalLoginModel : PageModel
{
    private readonly IConfiguration _config;

    public LocalLoginModel(IConfiguration config) => _config = config;

    public async Task<IActionResult> OnPostAsync(string username, string password)
    {
        var expectedUser = _config["Auth:LocalLogin:Username"];
        var expectedPass = _config["Auth:LocalLogin:Password"];

        if (string.IsNullOrEmpty(expectedUser) || string.IsNullOrEmpty(expectedPass) ||
            username != expectedUser || password != expectedPass)
        {
            return Redirect("/panel/Login?error=local");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
        };
        var identity = new ClaimsIdentity(claims, "ServerPanel");
        await HttpContext.SignInAsync("ServerPanel", new ClaimsPrincipal(identity));

        return LocalRedirect("~/panel");
    }
}
