using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Siga.Api.Contracts;
using Siga.Domain.Common;

namespace Siga.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected string TraceId => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    protected IActionResult HandleResult<T>(Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? Ok(ApiResponse.Ok(result.Value, TraceId)) : Failure(result.Error);
    }

    private ObjectResult Failure(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, ApiResponse.Fail(error, TraceId));
    }
}
