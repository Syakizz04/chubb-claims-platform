using Claims.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Claims.Api.Features;

/// <summary>N-5: maps domain exceptions to RFC 7807 ProblemDetails with consistent status codes.</summary>
public class ProblemDetailsExceptionHandler(IProblemDetailsService problems, ILogger<ProblemDetailsExceptionHandler> log)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        ProblemDetails? problem = ex switch
        {
            RequestValidationException v => new ValidationProblemDetails(v.Errors)
                { Status = 400, Title = "One or more fields are invalid." },
            DomainValidationException v => new ValidationProblemDetails(
                    new Dictionary<string, string[]> { [ToCamel(v.Field)] = [v.Message] })
                { Status = 400, Title = "One or more fields are invalid." },
            InvalidTransitionException t => new ProblemDetails
            {
                Status = 409, Title = "Action not allowed in the current status.", Detail = t.Message,
                Extensions = { ["currentStatus"] = t.Current.ToString(), ["action"] = t.Action.ToString() },
            },
            ConflictException c => new ProblemDetails { Status = 409, Title = "Conflict.", Detail = c.Message },
            ForbiddenException f => new ProblemDetails { Status = 403, Title = "Forbidden.", Detail = f.Message },
            NotFoundException n => new ProblemDetails { Status = 404, Title = "Not found.", Detail = n.Message },
            BadHttpRequestException b => new ProblemDetails { Status = 400, Title = "Malformed request.", Detail = b.Message },
            _ => null,
        };

        if (problem is null)
        {
            log.LogError(ex, "Unhandled exception");
            return false; // fall back to the default 500 problem response
        }

        http.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = ex });
    }

    private static string ToCamel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
