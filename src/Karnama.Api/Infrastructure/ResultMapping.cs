using Karnama.Domain.Common;

namespace Karnama.Api.Infrastructure;

internal static class ResultMapping
{
    public static IResult ToProblem(this Error error, HttpContext http)
    {
        var status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status422UnprocessableEntity,
        };
        return Results.Problem(
            title: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code,
                ["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier,
            });
    }

    public static IResult ToHttp<T>(this Result<T> result, HttpContext http, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess ? (onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value)) : result.Error!.ToProblem(http);

    public static IResult ToHttp(this Result result, HttpContext http) =>
        result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem(http);
}
