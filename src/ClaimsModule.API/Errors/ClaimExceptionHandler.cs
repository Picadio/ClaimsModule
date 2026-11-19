using ClaimsModule.Application.Exceptions;
using ClaimsModule.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace ClaimsModule.API.Errors;

internal sealed class ClaimExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var result = exception switch
        {
            ValidationException validation => ValidationProblem(validation.Errors
                .GroupBy(error => error.PropertyName.Replace("Request.", "", StringComparison.Ordinal))
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).Distinct().ToArray())),
            ClaimRuleViolation rule => ValidationProblem(new Dictionary<string, string[]> { [rule.Field] = [rule.Message] }),
            IdempotencyConflictException conflict => Results.Problem(title: "Idempotency key conflict",
                detail: conflict.Message, statusCode: StatusCodes.Status409Conflict, type: "IdempotencyConflict"),
            _ => null
        };

        if (result is null)
        {
            return false;
        }

        await result.ExecuteAsync(context);
        return true;
    }

    private static IResult ValidationProblem(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity, type: "ValidationError");
}
