using Karnama.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Karnama.Api.Infrastructure;

/// <summary>Maps unexpected exceptions to ProblemDetails. Never leaks stack traces.</summary>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        int status;
        string title;
        string code;

        switch (exception)
        {
            case ConcurrencyConflictException:
                status = StatusCodes.Status409Conflict;
                title = exception.Message;
                code = "errors.concurrency";
                break;
            // A unique index refused a racing duplicate (double tap, two devices, two managers at once): the data is
            // safe, so this is a conflict for the caller to refresh — never a server error.
            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }:
                logger.LogInformation("Unique index {Constraint} refused a duplicate (trace {TraceId})", pg.ConstraintName, traceId);
                status = StatusCodes.Status409Conflict;
                title = "این مورد هم‌زمان ثبت شده یا تکراری است. صفحه را تازه کنید و دوباره امتحان کنید.";
                code = "errors.duplicate";
                break;
            case BadHttpRequestException bad:
                status = bad.StatusCode;
                title = "درخواست نامعتبر است.";
                code = "errors.badRequest";
                break;
            case OperationCanceledException when ct.IsCancellationRequested:
                status = 499;
                title = "درخواست لغو شد.";
                code = "errors.cancelled";
                break;
            default:
                logger.LogError(exception, "Unhandled exception (trace {TraceId})", traceId);
                status = StatusCodes.Status500InternalServerError;
                title = "خطای غیرمنتظره‌ای رخ داد. دوباره تلاش کنید؛ اگر تکرار شد کد پیگیری را به پشتیبانی بدهید.";
                code = "errors.unexpected";
                break;
        }

        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Extensions = { ["code"] = code, ["traceId"] = traceId } },
        });
    }
}
