using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ServerPanel.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _config;
        [BindProperty]
        public string Username { get; set; } = string.Empty;
        [BindProperty]
        public string Password { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;

        public LoginModel(IConfiguration config)
        {
            _config = config;
        }

        public IActionResult OnGet()
        {
            if (User.Identity?.IsAuthenticated == true)
                return Redirect("/panel");
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            var configuredUser = _config["Auth:Username"];
            var configuredPassword = _config["Auth:Password"];

            if (Username == configuredUser && Password == configuredPassword)
            {
                var claims = new List<Claim> { new Claim(ClaimTypes.Name, Username) };
                var identity = new ClaimsIdentity(claims, "ServerPanel");
                var principal = new ClaimsPrincipal(identity);
                await HttpContext.SignInAsync("ServerPanel", principal);
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return Redirect("/panel");
            }
            Error = "Usuario o contraseña incorrectos";
            return Page();
        }
    }
}
