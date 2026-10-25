using Karnama.Api.Infrastructure;
using Karnama.Application.Clients;
using Karnama.Application.Portal;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>N4: credential vault (staff + client portal), client profile and renewal request, package reminder settings.</summary>
internal static class CredentialEndpoints
{
    public static void MapCredentials(this IEndpointRouteBuilder app)
    {
        static IResult NoStore(HttpContext http, IResult result)
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            return result;
        }

        var byClient = app.MapGroup("/api/v1/clients/{clientId:guid}/credentials").WithTags("Credentials").AddEndpointFilter<ValidationFilter>();
        byClient.MapGet("/", async (Guid clientId, CredentialService svc, HttpContext http, CancellationToken ct) => (await svc.ListAsync(clientId, ct)).ToHttp(http))
            .RequirePermission(Permissions.CredentialsView, PermissionScope.All);
        byClient.MapPost("/", async (Guid clientId, UpsertCredentialRequest body, CredentialService svc, HttpContext http, CancellationToken ct) =>
            NoStore(http, (await svc.CreateAsync(clientId, body, ct)).ToHttp(http, r => Results.Created($"/api/v1/credentials/{r.Id}", r))))
            .RequirePermission(Permissions.CredentialsManage, PermissionScope.All);

        var one = app.MapGroup("/api/v1/credentials/{id:guid}").WithTags("Credentials").AddEndpointFilter<ValidationFilter>();
        one.MapPut("/", async (Guid id, UpsertCredentialRequest body, CredentialService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http))
            .RequirePermission(Permissions.CredentialsManage, PermissionScope.All);
        one.MapDelete("/", async (Guid id, CredentialService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http))
            .RequirePermission(Permissions.CredentialsManage, PermissionScope.All);
        one.MapPost("/reveal/start", async (Guid id, CredentialService svc, HttpContext http, CancellationToken ct) => NoStore(http, (await svc.RevealStartAsync(id, ct)).ToHttp(http)))
            .RequirePermission(Permissions.CredentialsReveal, PermissionScope.All).RequireRateLimiting(RateLimits.Otp);
        one.MapPost("/reveal", async (Guid id, RevealRequest body, CredentialService svc, HttpContext http, CancellationToken ct) => NoStore(http, (await svc.RevealAsync(id, body, ct)).ToHttp(http)))
            .RequirePermission(Permissions.CredentialsReveal, PermissionScope.All).RequireRateLimiting(RateLimits.Auth);

        var portal = app.MapGroup("/api/v1/portal").WithTags("Portal").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        portal.MapGet("/profile", async (PortalService svc, HttpContext http, CancellationToken ct) => (await svc.ProfileAsync(ct)).ToHttp(http));
        portal.MapPost("/renew", async (RenewRequest body, PortalService svc, HttpContext http, CancellationToken ct) => (await svc.RequestRenewalAsync(body, ct)).ToHttp(http));
        portal.MapGet("/credentials", async (CredentialService svc, HttpContext http, CancellationToken ct) => (await svc.PortalListAsync(ct)).ToHttp(http));
        portal.MapPost("/credentials", async (UpsertCredentialRequest body, CredentialService svc, HttpContext http, CancellationToken ct) =>
            NoStore(http, (await svc.PortalCreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/portal/credentials/{r.Id}", r))));
        portal.MapPut("/credentials/{id:guid}", async (Guid id, UpsertCredentialRequest body, CredentialService svc, HttpContext http, CancellationToken ct) =>
            NoStore(http, (await svc.PortalUpdateAsync(id, body, ct)).ToHttp(http)));
        portal.MapDelete("/credentials/{id:guid}", async (Guid id, CredentialService svc, HttpContext http, CancellationToken ct) => (await svc.PortalDeleteAsync(id, ct)).ToHttp(http));

        var notices = app.MapGroup("/api/v1/settings/package-notices").WithTags("Settings").AddEndpointFilter<ValidationFilter>();
        notices.MapGet("/", async (PackageNoticeSettingsStore store, CancellationToken ct) => Results.Ok(await store.GetAsync(ct)))
            .RequirePermission(Permissions.PackagesView, PermissionScope.All);
        notices.MapPut("/", async (PackageNoticeSettings body, PackageNoticeSettingsStore store, HttpContext http, CancellationToken ct) => (await store.SaveAsync(body, ct)).ToHttp(http))
            .RequirePermission(Permissions.PackagesManage, PermissionScope.All);
    }
}
