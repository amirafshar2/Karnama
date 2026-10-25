using Karnama.Api.Infrastructure;
using Karnama.Application.Attendance;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>M4 (+N1): clock in/out with verification, breaks, work timer, corrections, live presence, setup, office QR kiosk.</summary>
internal static class AttendanceEndpoints
{
    public static void MapAttendance(this IEndpointRouteBuilder app)
    {
        var self = app.MapGroup("/api/v1/attendance").WithTags("Attendance")
            .RequirePermission(Permissions.AttendanceSelf, PermissionScope.Own).AddEndpointFilter<ValidationFilter>();
        self.MapGet("/me", async (AttendanceService svc, CancellationToken ct) => Results.Ok(await svc.TodayAsync(ct)));
        self.MapPost("/clock-in", async (ClockRequest body, AttendanceService svc, HttpContext http, CancellationToken ct) => (await svc.ClockInAsync(body, ct)).ToHttp(http));
        self.MapPost("/clock-out", async (ClockRequest body, AttendanceService svc, HttpContext http, CancellationToken ct) => (await svc.ClockOutAsync(body, ct)).ToHttp(http));
        self.MapPost("/break/start", async (ClockRequest body, AttendanceService svc, HttpContext http, CancellationToken ct) => (await svc.BreakAsync(true, body, ct)).ToHttp(http));
        self.MapPost("/break/end", async (ClockRequest body, AttendanceService svc, HttpContext http, CancellationToken ct) => (await svc.BreakAsync(false, body, ct)).ToHttp(http));
        self.MapPost("/timer/{workItemId:guid}", async (Guid workItemId, AttendanceService svc, HttpContext http, CancellationToken ct) => (await svc.StartTimerAsync(workItemId, ct)).ToHttp(http));
        self.MapDelete("/timer", async (AttendanceService svc, CancellationToken ct) => Results.Ok(await svc.StopTimerAsync(ct)));
        self.MapPost("/corrections", async (CorrectionRequest body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.RequestCorrectionAsync(body, ct)).ToHttp(http));

        var view = app.MapGroup("/api/v1/attendance").WithTags("Attendance")
            .RequirePermission(Permissions.AttendanceView, PermissionScope.Own).AddEndpointFilter<ValidationFilter>();
        view.MapGet("/live", async (AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.LiveAsync(ct)).ToHttp(http));
        view.MapGet("/shifts", async (Guid? userId, DateOnly from, DateOnly to, bool? needsReview, AttendanceAdminService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ShiftsAsync(userId, from, to, needsReview ?? false, ct)).ToHttp(http));
        view.MapGet("/corrections", async (bool? pending, AttendanceAdminService svc, CancellationToken ct) => Results.Ok(await svc.CorrectionsAsync(pending ?? false, ct)));
        view.MapPost("/corrections/{id:guid}/decide", async (Guid id, DecisionRequest body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.DecideAsync(id, body, ct)).ToHttp(http));
        view.MapPost("/shifts/{id:guid}/review", async (Guid id, DecisionRequest body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.ReviewAsync(id, body, ct)).ToHttp(http));

        var setup = app.MapGroup("/api/v1/attendance/setup").WithTags("Attendance")
            .RequirePermission(Permissions.AttendancePoliciesManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        setup.MapGet("/", async (AttendanceAdminService svc, CancellationToken ct) => Results.Ok(await svc.SetupAsync(ct)));
        setup.MapPut("/settings", async (AttendanceSettingsDto body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.SaveSettingsAsync(body, ct)).ToHttp(http));
        setup.MapPut("/schedules", async (UpsertScheduleRequest body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.SaveScheduleAsync(body, ct)).ToHttp(http));
        setup.MapPost("/policies", async (UpsertPolicyRequest body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.SavePolicyAsync(null, body, ct)).ToHttp(http));
        setup.MapPut("/policies/{id:guid}", async (Guid id, UpsertPolicyRequest body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.SavePolicyAsync(id, body, ct)).ToHttp(http));
        setup.MapDelete("/policies/{id:guid}", async (Guid id, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.DeletePolicyAsync(id, ct)).ToHttp(http));
        setup.MapPost("/places", async (UpsertPlaceRequest body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.AddPlaceAsync(body, ct)).ToHttp(http));
        setup.MapDelete("/places/{id:guid}", async (Guid id, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.DeletePlaceAsync(id, ct)).ToHttp(http));
        setup.MapPost("/holidays", async (UpsertHolidayRequest body, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.AddHolidayAsync(body, ct)).ToHttp(http));
        setup.MapDelete("/holidays/{id:guid}", async (Guid id, AttendanceAdminService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteHolidayAsync(id, ct)).ToHttp(http));

        // The office kiosk: a tablet logged in with the kiosk role shows this QR, refreshed every few seconds.
        app.MapGet("/api/v1/attendance/kiosk/code", async (AttendanceService svc, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await svc.KioskCodeAsync(ct));
        }).WithTags("Attendance").RequirePermission(Permissions.AttendanceKiosk, PermissionScope.All);
    }
}
