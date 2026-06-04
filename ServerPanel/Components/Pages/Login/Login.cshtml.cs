using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ServerPanel.Pages
{
    public class LoginModel : PageModel
    {
        public IActionResult OnGet()
        {
            if (User.Identity?.IsAuthenticated == true)
                return Redirect("/panel");
            return Page();
        }
    }
}
