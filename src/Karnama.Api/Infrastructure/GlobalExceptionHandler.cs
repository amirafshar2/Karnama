using Karnama.Application.Common;
using Microsoft.AspNetCore.Diagnostics;

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
