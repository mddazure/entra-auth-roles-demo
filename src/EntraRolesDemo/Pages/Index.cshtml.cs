using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EntraRolesDemo.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnPostSignIn() => Challenge(
        new AuthenticationProperties { RedirectUri = "/Identity" },
        OpenIdConnectDefaults.AuthenticationScheme);
}
