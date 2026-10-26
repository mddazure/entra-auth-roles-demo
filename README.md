# Microsoft Entra authentication and app roles demo

A C# ASP.NET Core Razor Pages application that demonstrates **authentication**, **identity claims**, and **server-side app-role authorization**. Run it locally with .NET 10 or package it as a Linux container for Azure App Service.

> This is an educational demo. Claims contain personal information. The app shows claims from a validated sign-in, not raw tokens, and never asks for your Entra password.

## What you can demonstrate

| Page | Access | Demonstration |
| --- | --- | --- |
| Home (`/`) | Public | Sign-in button and an explanation of the flow |
| My identity (`/Identity`) | Authenticated users | Identity, tenant, assigned roles, and Viewer access decision |
| Claims explorer (`/Claims`) | Authenticated users | Searchable claim names and values, with `roles` highlighted |
| Viewer area (`/Viewer`) | Authenticated users with `Viewer` | A real server-side authorization policy |
| Access denied (`/AccessDenied`) | Public | An explanatory page with HTTP 403 |
| Health (`/health`) | Public | Application liveness; does not check Entra or credentials |

The Viewer link remains visible to signed-in users without the role so you can demonstrate access denial. Entering `/Viewer` directly does not bypass authorization.

## How sign-in works

1. You select **Sign in with Microsoft**.
2. ASP.NET Core redirects your browser to Microsoft Entra ID using OpenID Connect authorization-code flow with PKCE.
3. You authenticate with Entra, including MFA when required.
4. Entra sends an authorization code back through your browser to `/signin-oidc`, normally in a form POST.
5. The server exchanges the code for tokens, authenticating the application with its client credential and supplying its PKCE verifier.
6. Microsoft.Identity.Web and ASP.NET Core validate the ID token. The app creates an encrypted, authenticated, HTTP-only session cookie.
7. Subsequent requests use this cookie. The Viewer authorization policy checks the signed-in principal for the exact role value `Viewer`.

The **client secret authenticates the application**, not the user. The client ID identifies the app but is public. A confidential server-side app needs a credential when redeeming its authorization code; PKCE does not replace that requirement.

The app deliberately disables inbound claim-name mapping and sets the role claim type to `roles`. This keeps the explorer readable and makes `User.IsInRole("Viewer")` agree with the authorization policy. Claims displayed after sign-in come from the authentication ticket; this is not an untouched JWT payload.

The app does not call Microsoft Graph, request group claims, or use an API access token to authorize its pages. It does not need a database. If you add a separate API later, validate an access token intended for that API and enforce its permissions there; do not send this web app's ID token as an API bearer token.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- A Microsoft Entra workforce tenant and permission to register an application.
- Permission to manage the application's user and role assignments, such as an appropriate application administrator role or ownership of the service principal.
- Two test users are useful: one assigned `Viewer`, one without that role.
- Docker with Linux containers to build and run the image.
- An Azure subscription only if you host the container in App Service.

Group-based application assignment requires **Microsoft Entra ID P1 or P2**. Direct user assignment is the simplest starting point. Nested group membership does not grant access through a group assignment.

## Register the application in Entra

### Create a single-tenant web application

1. Open the [Microsoft Entra admin center](https://entra.microsoft.com).
2. Go to **Entra ID > App registrations > New registration**.
3. Enter a name such as `Entra roles demo`.
4. Select **Accounts in this organizational directory only**.
5. Choose **Web** as the redirect URI platform and enter:

   ```text
   https://localhost:7066/signin-oidc
   ```

6. Select **Register** and record the **Directory (tenant) ID** and **Application (client) ID**.
7. Under **Authentication**, add any other Web redirect URIs you need:

   | Environment | Sign-in redirect URI |
   | --- | --- |
   | Local .NET | `https://localhost:7066/signin-oidc` |
   | Local Docker, using the instructions below | `https://localhost:8443/signin-oidc` |
   | App Service | `https://<your-actual-app-hostname>/signin-oidc` |

8. To support the sign-out callback, also register the corresponding Web redirect URI ending in `/signout-callback-oidc` for each environment.

Use the actual default hostname from your Web App's overview or your configured custom domain. Redirect URIs must match the scheme, hostname, port, and path used by the app.

Do **not** enable implicit ID-token or access-token grants. This application uses authorization-code flow. You do not need to expose an API or add Microsoft Graph permissions for this demo. An automatically added Graph `User.Read` permission is not used by the application.

### Create the Viewer role

Under **App registrations > your app > App roles > Create app role**, set:

| Field | Value |
| --- | --- |
| Display name | `Viewer` |
| Allowed member types | **Users/Groups** |
| Value | `Viewer` |
| Description | `Can view protected demo content` |
| Enable this app role | Selected |

Select **Apply**. The role **value** is what the application checks, and matching is case-sensitive. A differently named role, such as `Administrator`, does not automatically include `Viewer`.

### Assign a user or security group

Define roles in **App registrations**, but assign them through the corresponding **Enterprise application** (the service principal in your tenant).

1. Go to **Entra ID > Enterprise applications > your app**.
2. Open **Users and groups > Add user/group**.
3. Select the user or security group.
4. Under **Select a role**, select **Viewer**.
5. Select **Assign**.

For a security-group assignment, direct user members receive the role. The claim contains `Viewer`, not the group's name or ID. You do not need to configure group claims.

Under the enterprise application's **Properties**, leave **Assignment required?** set to **No** if you want both assigned and unassigned test users to sign in. Tenant consent policies and other access policies still apply. With **Yes**, Entra blocks unassigned users before the application can demonstrate its own role-based access denial, and administrator consent is required.

These are application roles, not Azure subscription/resource RBAC roles or Entra directory administrator roles.

### Create a development client secret

1. Open **App registrations > your app > Certificates and secrets > Client secrets**.
2. Create a short-lived secret.
3. Copy its **Value**, not its Secret ID. You cannot retrieve the value later.
4. Store it using User Secrets or external configuration as described below.

Never put the value in `appsettings.json`, this README, a Dockerfile, an image build argument, or source control. This implementation uses a secret for simplicity. A certificate or supported federated client assertion can replace it with the appropriate identity-library configuration and validation changes; an Azure managed identity does not automatically replace this app registration's sign-in credential.

## Run locally with .NET

Run these commands in PowerShell from the repository root:

```powershell
dotnet dev-certs https --trust
dotnet user-secrets set "AzureAd:TenantId" "<tenant-guid>" --project .\src\EntraRolesDemo
dotnet user-secrets set "AzureAd:ClientId" "<application-client-guid>" --project .\src\EntraRolesDemo
$secret = Read-Host "Entra client secret value" -MaskInput
dotnet user-secrets set "AzureAd:ClientSecret" $secret --project .\src\EntraRolesDemo
Remove-Variable secret
dotnet run --project .\src\EntraRolesDemo --launch-profile https
```

Open **https://localhost:7066** and select **Sign in with Microsoft**.

User Secrets are a development convenience outside the project, **not an encrypted production secret store**. The application rejects missing or malformed tenant/client IDs and a missing client secret at startup. Syntactically valid but incorrect settings fail during sign-in.

## Build and run the container locally

The root `Dockerfile` uses a .NET SDK build stage and an ASP.NET runtime stage. The final image runs as the non-root `app` user. No credentials are copied into the image.

The Docker Hub image name is **`madedroo/entra-auth-roles-demo:latest`**. To use the published image instead of building it, run:

```powershell
docker pull madedroo/entra-auth-roles-demo:latest
docker tag madedroo/entra-auth-roles-demo:latest entra-roles-demo:local
```

Build from the repository root:

```powershell
docker build --pull -t entra-roles-demo:local .
```

If your environment requires an approved NuGet mirror, supply its HTTPS service-index URL with
`--build-arg NUGET_SOURCE=<approved-feed-url>`. Do not put feed credentials in build arguments
or disable TLS certificate verification.

For a complete local container sign-in, use HTTPS so the browser can send the secure session, nonce, and correlation cookies. The following example mounts a development certificate from outside the workspace and stores Data Protection keys in a named volume:

```powershell
$certDir = Join-Path $env:TEMP "entra-roles-demo-https"
New-Item -ItemType Directory -Force -Path $certDir | Out-Null
$env:Kestrel__Certificates__Default__Password = Read-Host "Choose a temporary certificate password" -MaskInput
dotnet dev-certs https --trust
dotnet dev-certs https --export-path (Join-Path $certDir "localhost.pfx") --password $env:Kestrel__Certificates__Default__Password

$env:AzureAd__TenantId = "<tenant-guid>"
$env:AzureAd__ClientId = "<application-client-guid>"
$env:AzureAd__ClientSecret = Read-Host "Entra client secret value" -MaskInput

docker run --rm --name entra-roles-demo -p 8443:8443 `
  --env AzureAd__TenantId `
  --env AzureAd__ClientId `
  --env AzureAd__ClientSecret `
  --env Kestrel__Certificates__Default__Password `
  --env "Kestrel__Certificates__Default__Path=/https/localhost.pfx" `
  --env "Kestrel__Endpoints__Https__Url=https://+:8443" `
  --env "Hosting__PublicOrigin=https://localhost:8443" `
  --mount "type=bind,source=$certDir,target=/https,readonly" `
  --mount "type=volume,source=entra-roles-demo-keys,target=/home/data/entra-roles-keys" `
  entra-roles-demo:local
```

Open **https://localhost:8443**. Ensure this environment's sign-in and sign-out callback URIs are registered in Entra.

Stop the foreground container with Ctrl+C. Remove sensitive environment variables afterward:

```powershell
Remove-Item Env:\AzureAd__ClientSecret
Remove-Item Env:\Kestrel__Certificates__Default__Password
```

The certificate remains in the temporary directory until you remove it. Environment variables are visible to administrators and Docker operators; use a dedicated test credential and workstation.

## Host in Azure App Service

These instructions describe configuration; this project does not create resources or deploy automatically.

1. Push the image to a container registry that your Web App can access. Prefer managed identity for registry pulls where supported; this identity is separate from the Entra sign-in client credential.
2. Create a **Linux Web App** using a **container** publishing option and select your image.
3. Configure the main container to use **8080**, matching `ASPNETCORE_HTTP_PORTS` in the image. In the sidecar-enabled container model, use the main container's **target port**. In classic custom-container mode, set `WEBSITES_PORT=8080`; that setting does not apply to sidecar-enabled apps.
4. Turn **HTTPS Only** on. App Service provides the public HTTPS endpoint; the container uses HTTP internally. Do not deploy your local development certificate.
5. Keep **App Service Authentication / Easy Auth disabled** for this demo. Microsoft.Identity.Web in the application owns authentication.
6. Add the settings below in the Web App's environment variables/application settings.
7. Enable persistent `/home` storage for the chosen container model; in classic Linux custom-container mode set `WEBSITES_ENABLE_APP_SERVICE_STORAGE=true`. The `/home/data/entra-roles-keys` directory must be writable by the image's non-root user. Verify permissions after the volume is mounted; a mount can replace image-layer ownership.
8. Register the public sign-in and sign-out callback URIs in Entra, then restart the app.
9. Optionally configure App Service's health check path as `/health`.

| Setting | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AzureAd__TenantId` | Your tenant GUID |
| `AzureAd__ClientId` | Your application/client GUID |
| `AzureAd__ClientSecret` | A secret supplied securely, preferably through a Key Vault reference |
| `Hosting__PublicOrigin` | `https://<your-actual-app-hostname>` without any path |
| `AllowedHosts` | `<your-actual-app-hostname>` without a scheme; use semicolons to allow multiple known hostnames |
| `DataProtection__KeysPath` | `/home/data/entra-roles-keys` (already the image default) |

`Hosting__PublicOrigin` pins the Entra sign-in and sign-out callback URLs to the public HTTPS origin. This prevents internal HTTP hosting from generating an incorrect callback URL and does not require trusting arbitrary forwarded headers. Use one canonical origin per deployment and access the app through that origin; different domains do not share correlation cookies.

If you additionally need ASP.NET Core to recognize the original request scheme for other features, configure **only verified proxy IP addresses** through `ReverseProxy__KnownProxies__0`, `ReverseProxy__KnownProxies__1`, and so on. The app processes only `X-Forwarded-Proto` from trusted proxies; it never clears the framework's proxy trust restrictions.

Keep the **same Data Protection key ring across restarts and instances**. Keys protect authentication and antiforgery cookies, including authentication tickets that contain personal claims. File-based persistence here does not configure key encryption at rest: restrict storage access and use an appropriately protected shared key store for production. Do not assume that a container filesystem or an arbitrary volume is durable or encrypted. For a hardened deployment, consider shared Blob Storage keys protected with Key Vault.

## Demonstration walkthrough

1. Assign test user A the `Viewer` app role. Leave test user B without an app role.
2. Sign in as A. Open **My identity** and **Claims explorer**. Find the `roles` value `Viewer`.
3. Open **Viewer area**. You should see **Access granted**.
4. Sign out, then sign in as B. Use separate browser profiles if Entra's existing session makes account switching confusing.
5. B can inspect their own identity and claims, but `/Viewer` redirects to **Access denied**, whose final response is HTTP 403.
6. Assign B to `Viewer`, directly or through a security group. Allow assignment changes to propagate, then sign out and sign in again.

The relevant ID-token payload fragment for an assigned user is:

```json
{
  "roles": ["Viewer"]
}
```

The token can contain multiple roles. A missing `roles` claim means no app role was supplied for this sign-in; it does not mean authentication failed.

Role changes do not update issued tokens or an existing application cookie immediately. The demo uses a one-hour, non-sliding authentication cookie and does not implement continuous role revalidation. Signing out and in obtains fresh claims once Entra reflects the change.

## Build and test

```powershell
dotnet test .\EntraRolesDemo.slnx
dotnet publish .\src\EntraRolesDemo -c Release
dotnet format .\EntraRolesDemo.slnx --verify-no-changes
```

Integration tests exercise real ASP.NET Core cookie authentication and page authorization using test-issued protected cookies. OIDC discovery metadata is supplied in the test host so the suite does not contact a tenant. There is no fake-login endpoint or authentication bypass in the shipped application.

The suite also completes an authorization-code callback against a stub token endpoint, validates a signed test ID token, and verifies the resulting cookie grants Viewer access. It checks that the public HTTPS callback remains correct behind TLS termination and that the real token handler preserves the original role claim name.

Other cases cover anonymous challenges, PKCE, role enforcement, missing roles, multiple roles, HTML encoding, no-store responses, antiforgery checks, and rejection of a forged callback. These tests do not replace an end-to-end sign-in against your real tenant or a container-host smoke test.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| App stops at startup with an options-validation error | Supply valid tenant/client GUIDs and the secret value through external configuration |
| Entra reports redirect URI mismatch | Compare the exact URI with the Web platform entries; set `Hosting__PublicOrigin` behind App Service |
| Entra reports an invalid client secret | Use the secret's **Value**, not its ID; check its expiration and restart after changing configuration |
| Missing `roles` claim | Define an enabled Users/Groups app role, assign it on the correct enterprise application, then obtain a fresh sign-in |
| Group assignment has no effect | Check P1/P2 licensing, direct user membership, and assignment to the correct role; nested membership is not supported |
| Viewer access denied despite another role | `Viewer` is an exact, independent role; there is no role hierarchy |
| Correlation/nonce error | Use HTTPS consistently, allow cookies, and keep the same hostname throughout sign-in; do not start on one hostname and return to another |
| Sessions fail after restart or intermittently across instances | Persist and share the Data Protection key ring; check storage permissions |
| App Service cannot start the container | Check image access, target port 8080, required settings, `/home` storage permissions, and container logs |
| Health check succeeds but sign-in fails | `/health` is liveness only; check actual Entra sign-in and token redemption separately |
| Browser gets a 400 before the page | Check `AllowedHosts` and use the configured canonical hostname |

Production errors display a request ID, not exception details. Review server logs and Entra sign-in logs without enabling token/PII logging. Keep development exception pages off on hosted deployments.

## Project layout

```text
EntraRolesDemo.slnx
Dockerfile
src\
  EntraRolesDemo\
    Program.cs             Authentication, authorization, hosting, and middleware
    EntraSettings.cs       Startup validation and Viewer policy name
    Pages\                 Razor Pages and page handlers
    wwwroot\               Local CSS and claim-filtering JavaScript
tests\
  EntraRolesDemo.Tests\     Integration tests
```

## Microsoft references

- [Web application sign-in flow](https://learn.microsoft.com/entra/identity-platform/v2-app-types#web-apps)
- [Authorization-code flow and token redemption](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-auth-code-flow)
- [ASP.NET Core sign-in quickstart](https://learn.microsoft.com/entra/identity-platform/quickstart-web-app-aspnet-core-sign-in)
- [Define app roles and receive them in tokens](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)
- [Assign users and groups to an enterprise application](https://learn.microsoft.com/entra/identity/enterprise-apps/assign-user-or-group-access-portal)
- [Configure App Service custom containers](https://learn.microsoft.com/azure/app-service/configure-custom-container)
- [App Service sidecar-enabled container configuration](https://learn.microsoft.com/azure/app-service/configure-sidecar)
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview)
- [ASP.NET Core proxy and load balancer configuration](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer)
