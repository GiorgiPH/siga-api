using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Siga.Api.Contracts;
using Siga.Api.Contracts.Auth;
using Siga.Api.Extensions;
using Siga.Application.Abstractions.Messaging;
using Siga.Application.Features.Auth;
using Siga.Application.Features.Auth.GetCurrentUser;
using Siga.Application.Features.Auth.Login;
using Siga.Application.Features.Auth.Logout;
using Siga.Application.Features.Auth.RefreshToken;
using Siga.Domain.Common;

namespace Siga.Api.Controllers.V1;

/// <summary>Autenticación de usuarios de la aplicación móvil.</summary>
[Route("api/v1/auth")]
[Tags("Autenticación")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Inicia sesión con usuario y contraseña.</summary>
    /// <remarks>
    /// Valida al usuario contra la tabla de usuarios del sistema. En el primer acceso desde la app,
    /// la contraseña se migra a un hash seguro. Tras 5 intentos fallidos la cuenta se bloquea 15 minutos.
    /// </remarks>
    /// <response code="200">Sesión iniciada. Devuelve access token, refresh token y perfil.</response>
    /// <response code="401">Usuario o contraseña incorrectos (AUTH_INVALID_CREDENTIALS).</response>
    /// <response code="403">Cuenta bloqueada, usuario inactivo o sin acceso a la app.</response>
    /// <response code="422">Datos de entrada inválidos.</response>
    /// <response code="429">Demasiados intentos desde la misma IP.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<ApiResponse<AuthTokensResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] ICommandHandler<LoginCommand, AuthTokensResponse> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        return HandleResult(await handler.Handle(request.ToCommand(), ct));
    }

    /// <summary>Renueva el access token usando un refresh token (rotación).</summary>
    /// <remarks>El refresh token usado queda revocado. Reusar un token revocado cierra todas las sesiones derivadas.</remarks>
    /// <response code="200">Tokens nuevos.</response>
    /// <response code="401">Refresh token inválido, expirado o revocado (AUTH_INVALID_REFRESH_TOKEN).</response>
    /// <response code="422">Datos de entrada inválidos.</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<ApiResponse<AuthTokensResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        [FromServices] ICommandHandler<RefreshTokenCommand, AuthTokensResponse> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        return HandleResult(await handler.Handle(request.ToCommand(), ct));
    }

    /// <summary>Cierra la sesión del dispositivo actual.</summary>
    /// <response code="200">Sesión cerrada (operación idempotente).</response>
    /// <response code="401">No autenticado.</response>
    [HttpPost("logout")]
    [ProducesResponseType<ApiResponse<Success>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        [FromServices] ICommandHandler<LogoutCommand, Success> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        return HandleResult(await handler.Handle(request.ToCommand(), ct));
    }

    /// <summary>Devuelve el perfil del usuario autenticado.</summary>
    /// <response code="200">Perfil del usuario.</response>
    /// <response code="401">No autenticado o token expirado.</response>
    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<UserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(
        [FromServices] IQueryHandler<GetCurrentUserQuery, UserProfileResponse> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return HandleResult(await handler.Handle(new GetCurrentUserQuery(), ct));
    }
}
