using ClaimsModule.API.Correlation;
using Microsoft.AspNetCore.Mvc;

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
}
