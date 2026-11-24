using ClaimsModule.API.Correlation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ClaimsModule.API.Errors;

internal static class ApiProblems
{
    public const string ValidationError = "ValidationError";

    public static IActionResult InvalidModelState(ActionContext context)
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Type = ValidationError
        };
        AddCorrelation(context.HttpContext, problem);
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }

    public static void AddCorrelation(HttpContext context, ProblemDetails problem) =>
        problem.Extensions["correlationId"] = RequestCorrelation.Read(context);

    public static Task WriteStatusCodeAsync(HttpContext context)
    {
        var status = context.Response.StatusCode;
        var type = status switch
        {
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "NotFound",
            StatusCodes.Status429TooManyRequests => "TooManyRequests",
            _ => "about:blank"
        };
        var title = ReasonPhrases.GetReasonPhrase(status);
        var problem = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = string.IsNullOrEmpty(title) ? "Request failed" : title
        };
        AddCorrelation(context, problem);
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
