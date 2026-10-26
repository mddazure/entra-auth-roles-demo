using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace EntraRolesDemo.Tests;

public sealed class DemoFactory : WebApplicationFactory<Program>
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public SecurityKey? SigningKey { get; set; }
    public HttpMessageHandler? BackchannelHandler { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("AzureAd:TenantId", TenantId);
        builder.UseSetting("AzureAd:ClientId", "22222222-2222-2222-2222-222222222222");
        builder.UseSetting("AzureAd:ClientSecret", "test-only-not-a-real-credential");
        builder.ConfigureTestServices(services =>
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0",
                    AuthorizationEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/authorize",
                    TokenEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token",
                    EndSessionEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/logout"
                };
                if (SigningKey is not null) configuration.SigningKeys.Add(SigningKey);
                if (BackchannelHandler is not null) options.Backchannel = new HttpClient(BackchannelHandler);
                options.TokenValidationParameters.ValidIssuer = configuration.Issuer;
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
    }

    public HttpClient NewClient(params Claim[] claims)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        if (claims.Length > 0)
        {
            // Exercise the real cookie middleware, without introducing a test authentication path into the app.
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims,
                CookieAuthenticationDefaults.AuthenticationScheme, "name", "roles"));
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            }, CookieAuthenticationDefaults.AuthenticationScheme);
            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        }
        return client;
    }
}

public sealed class ApplicationTests(DemoFactory factory) : IClassFixture<DemoFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    public async Task Public_endpoints_are_available_without_authentication(string path)
    {
        using var client = factory.NewClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/Identity")]
    [InlineData("/Claims")]
    [InlineData("/Viewer")]
    public async Task Protected_pages_redirect_anonymous_users_to_Entra(string path)
    {
        using var client = factory.NewClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("login.microsoftonline.com", response.Headers.Location?.Host);
        var query = WebUtility.UrlDecode(response.Headers.Location!.Query);
        Assert.Contains("response_type=code", query);
        Assert.Contains("code_challenge_method=S256", query);
        Assert.Contains("redirect_uri=https://localhost/signin-oidc", query);
        Assert.DoesNotContain("client_secret", query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("viewer")]
    [InlineData("Administrator")]
    public async Task Authenticated_users_without_the_exact_Viewer_role_are_denied(string? role)
    {
        var claims = new List<Claim> { new("name", "Demo user") };
        if (role is not null) claims.Add(new Claim("roles", role));
        using var client = factory.NewClient(claims.ToArray());
        using var response = await client.GetAsync("/Viewer");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessDenied", response.Headers.Location?.AbsolutePath);
        using var denied = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("Signed in does not mean allowed", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Viewer_role_grants_access_even_alongside_other_roles()
    {
        using var client = factory.NewClient(new("name", "Demo user"),
            new("roles", "Editor"), new("roles", "Viewer"));
        using var response = await client.GetAsync("/Viewer");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Protected demonstration content", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Claims_explorer_displays_roles_and_encodes_claim_values()
    {
        using var client = factory.NewClient(new("name", "<script>alert(1)</script>"), new("roles", "Viewer"));
        using var response = await client.GetAsync("/Claims");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Viewer", html);
        Assert.Contains("role-row", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Signed_in_users_without_roles_can_inspect_their_identity()
    {
        using var client = factory.NewClient(new Claim("name", "Demo user"));
        using var identity = await client.GetAsync("/Identity");
        using var claims = await client.GetAsync("/Claims");
        Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        Assert.Equal(HttpStatusCode.OK, claims.StatusCode);
        Assert.Contains("No app roles assigned", await identity.Content.ReadAsStringAsync());
        Assert.Contains("No roles claim present", await claims.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/?handler=SignIn")]
    [InlineData("/Identity?handler=SignOut")]
    public async Task Authentication_actions_reject_posts_without_antiforgery_tokens(string path)
    {
        using var client = factory.NewClient(new Claim("name", "Demo user"));
        using var response = await client.PostAsync(path, new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_button_starts_the_real_OIDC_challenge()
    {
        using var client = factory.NewClient();
        using var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ",
            page.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0])));
        using var response = await client.PostAsync("/?handler=SignIn", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value) }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("login.microsoftonline.com", response.Headers.Location?.Host);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.Contains(".AspNetCore.Correlation.") && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Forged_callback_does_not_create_a_session()
    {
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/signin-oidc?code=forged&state=forged");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(cookie => cookie.StartsWith("__Host-EntraRolesDemo=")));
    }

    [Fact]
    public async Task Code_redemption_creates_a_session_with_the_Viewer_role_behind_TLS_termination()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-signing-key" };
        using var tokenEndpoint = new TokenEndpointStub(key);
        using var loginFactory = new DemoFactory { SigningKey = key, BackchannelHandler = tokenEndpoint };
        using var host = loginFactory.WithWebHostBuilder(builder =>
            builder.UseSetting("Hosting:PublicOrigin", "https://public.example.test"));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var challenge = await client.GetAsync("/Viewer");
        var query = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        Assert.Equal("https://public.example.test/signin-oidc", query["redirect_uri"]);
        tokenEndpoint.Nonce = query["nonce"].ToString();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ",
            challenge.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0])));

        using var callback = await client.PostAsync("/signin-oidc", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["code"] = "test-authorization-code", ["state"] = query["state"].ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/Viewer", callback.Headers.Location?.OriginalString);
        Assert.NotNull(tokenEndpoint.RedeemedForm);
        Assert.Equal("test-authorization-code", tokenEndpoint.RedeemedForm["code"]);
        Assert.Equal("test-only-not-a-real-credential", tokenEndpoint.RedeemedForm["client_secret"]);
        Assert.Equal("https://public.example.test/signin-oidc", tokenEndpoint.RedeemedForm["redirect_uri"]);
        Assert.False(string.IsNullOrWhiteSpace(tokenEndpoint.RedeemedForm["code_verifier"]));

        var cookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-EntraRolesDemo="));
        var cookieOptions = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var session = cookieOptions.TicketDataFormat.Unprotect(cookie.Split(';')[0].Split('=', 2)[1]);
        Assert.NotNull(session);
        Assert.Equal("roles", ((ClaimsIdentity)session.Principal.Identity!).RoleClaimType);
        Assert.True(session.Principal.IsInRole("Viewer"), string.Join(", ",
            session.Principal.Claims.Select(claim => $"{claim.Type}={claim.Value}")));
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        using var viewer = await client.GetAsync("/Viewer");
        using var claims = await client.GetAsync("/Claims");
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, claims.StatusCode);
        Assert.Contains("Viewer", await claims.Content.ReadAsStringAsync());
        Assert.Contains("Test sign-in user", await claims.Content.ReadAsStringAsync());
    }
}

internal sealed class TokenEndpointStub(SecurityKey signingKey) : HttpMessageHandler
{
    public string Nonce { get; set; } = "";
    public Dictionary<string, string>? RedeemedForm { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RedeemedForm = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken))
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: $"https://login.microsoftonline.com/{DemoFactory.TenantId}/v2.0",
            audience: "22222222-2222-2222-2222-222222222222",
            claims:
            [
                new("sub", "test-subject"), new("oid", "33333333-3333-3333-3333-333333333333"),
                new("tid", DemoFactory.TenantId), new("name", "Test sign-in user"),
                new("roles", "Viewer"), new("nonce", Nonce),
                new("iat", EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64)
            ],
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(10),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                token_type = "Bearer",
                expires_in = 600,
                access_token = "opaque-test-access-token",
                id_token = new JwtSecurityTokenHandler().WriteToken(token)
            }), System.Text.Encoding.UTF8, "application/json")
        };
    }
}
