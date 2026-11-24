using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Siga.Api.Contracts;
using Siga.Api.ErrorHandling;
using Siga.Api.OpenApi;
using Siga.Api.Services;
using Siga.Application.Abstractions.Security;
using Siga.Domain.Common;
using Siga.Infrastructure.Persistence;
using Siga.Infrastructure.Security;

namespace Siga.Api.Extensions;

internal static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IRequestContext, HttpRequestContext>();

        services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
            {
                var errors = ctx.ModelState
                    .Where(kv => kv.Value?.Errors.Count > 0)
                    .SelectMany(kv => kv.Value!.Errors.Select(e => new FieldError(
                        JsonNamingPolicy.CamelCase.ConvertName(kv.Key.TrimStart('$', '.')),
                        "INVALID_FORMAT", "El valor enviado no tiene un formato válido.")))
                    .ToList();
                var traceId = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
                return new BadRequestObjectResult(ApiResponse.Fail("BAD_REQUEST",
                    "La petición no tiene un formato válido.", traceId, errors));
            });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        // Autenticación JWT
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // AJUSTE: los parámetros se toman de JwtOptions de forma diferida (validadas con ValidateOnStart)
        // en lugar de leer la configuración al registrar servicios; así los tests pueden sobrescribirla.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((o, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = AuthResponseWriter.WriteChallengeAsync,
                    OnForbidden = AuthResponseWriter.WriteForbiddenAsync
                };
            });
        services.AddAuthorization();

        // Rate limiting
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                var traceId = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
                await ctx.HttpContext.Response.WriteAsJsonAsync(ApiResponse.Fail("RATE_LIMITED",
                    "Demasiadas peticiones. Espera un momento e intenta de nuevo.", traceId), ct);
            };
            o.AddPolicy(RateLimitPolicies.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(RateLimitPolicies.PerUser, http => RateLimitPartition.GetTokenBucketLimiter(
                http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 100, TokensPerPeriod = 50, ReplenishmentPeriod = TimeSpan.FromSeconds(10), QueueLimit = 0
                }));
        });

        // Detrás de IIS / proxy / balanceador: IP real del cliente
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // PRODUCCIÓN: registrar aquí las IP del proxy en o.KnownProxies / o.KnownIPNetworks
        });

        services.AddHealthChecks().AddDbContextCheck<SigaDbContext>("sqlserver", tags: ["ready"]);
        services.AddApiDocumentation();
        return services;
    }
}
