using Microsoft.AspNetCore.Http;

namespace WildCat.Admin;

/// <summary>
/// RFC 7807 error responses. Every error carries a <c>detail</c> the panel can show as is;
/// validation errors also list each message in an <c>errors</c> array.
/// </summary>
internal static class Problems
{
    public const string ValidationTitle = "The request is not valid.";

    public static IResult Validation(IReadOnlyCollection<string> errors)
    {
        return TypedResults.Problem(
            detail: string.Join(" ", errors),
            statusCode: StatusCodes.Status400BadRequest,
            title: ValidationTitle,
            extensions: new Dictionary<string, object?> { ["errors"] = errors.ToArray() });
    }

    public static IResult Validation(string error) => Validation([error]);

    public static IResult BadRequest(string detail) => Problem(StatusCodes.Status400BadRequest, detail);

    public static IResult Unauthorized(string detail) => Problem(StatusCodes.Status401Unauthorized, detail);

    public static IResult Forbidden(string detail) => Problem(StatusCodes.Status403Forbidden, detail);

    public static IResult NotFound(string detail) => Problem(StatusCodes.Status404NotFound, detail);

    public static IResult Conflict(string detail) => Problem(StatusCodes.Status409Conflict, detail);

    public static IResult Problem(int status, string detail) => TypedResults.Problem(detail: detail, statusCode: status);
}
