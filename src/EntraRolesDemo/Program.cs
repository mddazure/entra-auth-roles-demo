using System.Net;
using EntraRolesDemo;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.JsonWebTokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<EntraSettings>()
    .BindConfiguration("AzureAd")
    .Validate(settings => Guid.TryParse(settings.TenantId, out var id) && id != Guid.Empty,
        "AzureAd:TenantId must be your directory's tenant GUID.")
    .Validate(settings => Guid.TryParse(settings.ClientId, out var id) && id != Guid.Empty,
        "AzureAd:ClientId must be your app registration's application GUID.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.ClientSecret),
        "Set AzureAd:ClientSecret through User Secrets or an environment variable, never source control.")
    .ValidateOnStart();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.ResponseType = "code";
    options.UsePkce = true;
    options.MapInboundClaims = false;
    // Microsoft.Identity.Web can replace the framework's default token handler.
    if (options.TokenHandler is JsonWebTokenHandler handler)
    {
        handler.MapInboundClaims = false;
    }
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "roles";
    options.SaveTokens = false;
    var publicOrigin = builder.Configuration["Hosting:PublicOrigin"];
    if (!string.IsNullOrWhiteSpace(publicOrigin))
    {
        if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme != Uri.UriSchemeHttps || origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) ||
            !string.IsNullOrEmpty(origin.UserInfo))
        {
            throw new InvalidOperationException("Hosting:PublicOrigin must be an HTTPS origin without a path, query, fragment, or credentials.");
        }

        // Pin callback URLs behind TLS termination without trusting arbitrary forwarded headers.
        var redirect = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            await redirect(context);
            context.ProtocolMessage.RedirectUri = new Uri(origin, options.CallbackPath.Value!).AbsoluteUri;
        };
        var signOut = options.Events.OnRedirectToIdentityProviderForSignOut;
        options.Events.OnRedirectToIdentityProviderForSignOut = async context =>
        {
            await signOut(context);
            context.ProtocolMessage.PostLogoutRedirectUri =
                new Uri(origin, options.SignedOutCallbackPath.Value!).AbsoluteUri;
        };
    }
});
builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "__Host-EntraRolesDemo";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.AccessDeniedPath = "/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(1);
    options.SlidingExpiration = false;
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(DemoPolicies.Viewer, policy =>
        policy.RequireAuthenticatedUser().RequireRole("Viewer"));
});
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

var protection = builder.Services.AddDataProtection().SetApplicationName("EntraRolesDemo");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

var app = builder.Build();
if (string.IsNullOrWhiteSpace(keysPath) && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning("DataProtection:KeysPath is not set. Configure a persistent, protected key ring before hosting.");
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; " +
        "form-action 'self' https://login.microsoftonline.com; object-src 'none'";
    await next(context);
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

public partial class Program;
