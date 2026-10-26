using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EntraRolesDemo.Pages;

public class ErrorModel : PageModel
{
    public string RequestId { get; private set; } = "";

    public void OnGet()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    public void OnPost() => OnGet();
}
