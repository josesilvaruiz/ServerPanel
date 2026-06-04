using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ServerPanel.Pages;

public class SteamLoginModel : PageModel
{
    public IActionResult OnGet()
    {
        var props = new AuthenticationProperties { RedirectUri = "/SteamCallback" };
        return Challenge(props, "Steam");
    }
}
