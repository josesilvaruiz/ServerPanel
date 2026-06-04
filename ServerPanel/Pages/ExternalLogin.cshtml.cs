using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ServerPanel.Pages;

public class ExternalLoginModel : PageModel
{
    public IActionResult OnGet()
    {
        var props = new AuthenticationProperties { RedirectUri = "/ExternalCallback" };
        return Challenge(props, "Google");
    }
}
