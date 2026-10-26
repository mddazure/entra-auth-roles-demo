using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EntraRolesDemo.Pages;

[Authorize(Policy = DemoPolicies.Viewer)]
public class ViewerModel : PageModel;
