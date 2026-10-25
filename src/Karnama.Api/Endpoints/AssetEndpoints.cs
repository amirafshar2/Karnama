using Karnama.Api.Infrastructure;
using Karnama.Application.Assets;
using Karnama.Domain.Assets;

namespace Karnama.Api.Endpoints;

/// <summary>N3: equipment lent to staff. Who sees and does what is decided in <see cref="AssetService"/>.</summary>
internal static class AssetEndpoints
{
    public static void MapAssets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/assets").WithTags("Assets").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        g.MapGet("/", async (string? q, AssetStatus? status, Guid? category, Guid? holder, AssetService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ListAsync(new AssetFilter(q, status, category, holder), ct)).ToHttp(http));
        g.MapGet("/mine", async (AssetService svc, HttpContext http, CancellationToken ct) => (await svc.MineAsync(ct)).ToHttp(http));
        g.MapGet("/people", async (AssetService svc, HttpContext http, CancellationToken ct) => (await svc.PeopleAsync(ct)).ToHttp(http));
        g.MapGet("/tag/{tag}", async (string tag, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.ByTagAsync(tag, ct)).ToHttp(http));
        g.MapGet("/{id:guid}", async (Guid id, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        g.MapPost("/", async (SaveAssetRequest body, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.CreateAsync(body, ct)).ToHttp(http));
        g.MapPut("/{id:guid}", async (Guid id, SaveAssetRequest body, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        g.MapDelete("/{id:guid}", async (Guid id, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
        g.MapPost("/{id:guid}/status", async (Guid id, StatusRequest body, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.SetStatusAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/{id:guid}/handover", async (Guid id, HandOverRequest body, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.HandOverAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/{id:guid}/photos", async (Guid id, IFormFile file, AssetPhotoStage stage, Guid? assignmentId, AssetService svc, HttpContext http, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return (await svc.AddPhotoAsync(id, stage, assignmentId, s, file.Length, ct)).ToHttp(http);
        }).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(AssetService.MaxPhotoBytes + 64 * 1024));
        g.MapGet("/photos/{photoId:guid}", async (Guid photoId, AssetService svc, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.OpenPhotoAsync(photoId, ct);
            if (r.IsFailure) return r.ToHttp(http);
            http.Response.Headers.CacheControl = "private, max-age=86400";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Stream(r.Value.Content, r.Value.Photo.ContentType);
        });

        var x = g.MapGroup("/assignments/{assignmentId:guid}");
        x.MapPost("/accept", async (Guid assignmentId, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.AcceptAsync(assignmentId, ct)).ToHttp(http));
        x.MapPost("/decline", async (Guid assignmentId, DeclineRequest? body, AssetService svc, HttpContext http, CancellationToken ct) =>
            (await svc.DeclineAsync(assignmentId, body?.Note, ct)).ToHttp(http));
        x.MapPost("/cancel", async (Guid assignmentId, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.CancelAsync(assignmentId, ct)).ToHttp(http));
        x.MapPost("/return", async (Guid assignmentId, ReturnRequest body, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.SubmitReturnAsync(assignmentId, body, ct)).ToHttp(http));
        x.MapPost("/return/confirm", async (Guid assignmentId, ConfirmReturnRequest body, AssetService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ConfirmReturnAsync(assignmentId, body, ct)).ToHttp(http));
        x.MapPost("/transfer", async (Guid assignmentId, TransferRequest body, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.OfferTransferAsync(assignmentId, body, ct)).ToHttp(http));
        x.MapPost("/transfer/cancel", async (Guid assignmentId, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.CancelTransferAsync(assignmentId, ct)).ToHttp(http));
        x.MapPost("/transfer/accept", async (Guid assignmentId, AcceptTransferRequest body, AssetService svc, HttpContext http, CancellationToken ct) =>
            (await svc.AcceptTransferAsync(assignmentId, body, ct)).ToHttp(http));

        var c = app.MapGroup("/api/v1/asset-categories").WithTags("Assets").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        c.MapGet("/", async (AssetService svc, HttpContext http, CancellationToken ct) => (await svc.CategoriesAsync(ct)).ToHttp(http));
        c.MapPost("/", async (SaveCategoryRequest body, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.SaveCategoryAsync(null, body, ct)).ToHttp(http));
        c.MapPut("/{id:guid}", async (Guid id, SaveCategoryRequest body, AssetService svc, HttpContext http, CancellationToken ct) => (await svc.SaveCategoryAsync(id, body, ct)).ToHttp(http));
    }
}

internal sealed record DeclineRequest(string? Note);
