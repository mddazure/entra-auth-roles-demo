using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EntraRolesDemo.Pages;

public class ClaimsModel : PageModel
{
    public Claim[] Claims { get; private set; } = [];
    public string[] Roles { get; private set; } = [];
    public string RolesJson => JsonSerializer.Serialize(new { roles = Roles },
        new JsonSerializerOptions { WriteIndented = true });

    public void OnGet()
    {
        Claims = User.Claims.OrderBy(claim => claim.Type).ThenBy(claim => claim.Value).ToArray();
        Roles = User.FindAll("roles").Select(claim => claim.Value).Distinct().Order().ToArray();
    }
}
