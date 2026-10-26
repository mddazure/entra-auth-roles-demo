namespace EntraRolesDemo;

public sealed class EntraSettings
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public static class DemoPolicies
{
    public const string Viewer = "ViewerOnly";
}
