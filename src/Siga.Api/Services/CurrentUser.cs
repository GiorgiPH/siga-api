using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Siga.Application.Abstractions.Security;

namespace Siga.Api.Services;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string UserName => Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("No hay un usuario autenticado en el contexto.");
}
