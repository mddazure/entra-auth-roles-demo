using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EntraRolesDemo.Pages;

public class AccessDeniedModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status403Forbidden;
}
