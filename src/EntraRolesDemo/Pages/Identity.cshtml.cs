using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EntraRolesDemo.Pages;

public class IdentityModel : PageModel
{
    public string[] Roles { get; private set; } = [];

    public void OnGet() =>
        Roles = User.FindAll("roles").Select(claim => claim.Value).Distinct().Order().ToArray();

    public IActionResult OnPostSignOut() => SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme);
}
