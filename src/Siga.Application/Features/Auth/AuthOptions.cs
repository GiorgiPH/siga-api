using System.ComponentModel.DataAnnotations;

namespace Siga.Application.Features.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Range(1, 20)] public int MaxFailedAttempts { get; init; } = 5;
    [Range(1, 1440)] public int LockoutMinutes { get; init; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; init; } = 14;

    /// <summary>Si es true, cualquier usuario activo de dbo.Usuarios puede entrar a la app en su primer login.</summary>
    public bool AutoEnrollOnFirstLogin { get; init; } = true;

    /// <summary>Valores de dbo.Usuarios.Estatus que se consideran activos.</summary>
#pragma warning disable CA1819 // AJUSTE: arreglo requerido por el binder de configuración y [MinLength]
    [MinLength(1)] public string[] ActiveStatuses { get; init; } = [];
#pragma warning restore CA1819

    public bool IsActiveStatus(string? status) =>
        status is not null &&
        ActiveStatuses.Any(s => string.Equals(s.Trim(), status.Trim(), StringComparison.OrdinalIgnoreCase));
}
