using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Siga.Api.Contracts;

namespace Siga.Api.ErrorHandling;

/// <summary>Respuestas 401/403 del middleware JWT con el sobre estándar ApiResponse.</summary>
internal static class AuthResponseWriter
{
    public static async Task WriteChallengeAsync(JwtBearerChallengeContext ctx)
    {
        ctx.HandleResponse();
        var expired = ctx.AuthenticateFailure is SecurityTokenExpiredException;
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (expired) ctx.Response.Headers["X-Token-Expired"] = "true";

        await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail(
            expired ? "TOKEN_EXPIRED" : "UNAUTHORIZED",
            expired ? "La sesión expiró." : "Debes iniciar sesión.",
            Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));
    }

    public static async Task WriteForbiddenAsync(ForbiddenContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail("FORBIDDEN",
            "No tienes permiso para esta operación.", Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));
    }
}
