namespace Siga.Infrastructure.Persistence;

/// <summary>
/// AJUSTE: la cadena de conexión se resuelve de forma diferida (no al registrar servicios) para que
/// los tests de integración puedan sobrescribirla, y se valida al arrancar (ValidateOnStart).
/// </summary>
public sealed class DatabaseOptions
{
    public const string ConnectionStringName = "Administracion";

    public string? ConnectionString { get; set; }
}
