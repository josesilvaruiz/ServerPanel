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
        Console.WriteLine("=== ExternalCallback START ===");

        var result = await HttpContext.AuthenticateAsync("External");
        Console.WriteLine($"External auth success: {result.Succeeded}");
        if (!result.Succeeded)
        {
            Console.WriteLine($"Auth failure: {result.Failure?.Message}");
            return Redirect("/Login?error=google");
        }

        if (result.Principal != null)
            foreach (var c in result.Principal.Claims)
                Console.WriteLine($"  Claim: {c.Type} = {c.Value}");

        var email = result.Principal?.FindFirstValue(ClaimTypes.Email);
        Console.WriteLine($"Email: {email}");

        var allowed = _config.GetSection("Auth:AllowedEmails").Get<string[]>() ?? [];
        Console.WriteLine($"AllowedEmails: [{string.Join(", ", allowed)}]");

        await HttpContext.SignOutAsync("External");

        if (string.IsNullOrEmpty(email) || !allowed.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("UNAUTHORIZED - email not in allowed list");
            return Redirect("/Login?error=unauthorized");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Email, email)
        };
        var identity = new ClaimsIdentity(claims, "ServerPanel");
        Console.WriteLine("Signing in user...");
        await HttpContext.SignInAsync("ServerPanel", new ClaimsPrincipal(identity));

        Console.WriteLine("=== COOKIES SET BY RESPONSE ===");
        foreach (var cookie in Response.Headers.SetCookie)
            Console.WriteLine(cookie.ToString());
        Console.WriteLine("=== END COOKIES ===");

        Console.WriteLine("Redirecting to ~/panel");
        return LocalRedirect("~/panel");
    }
}
