namespace Siga.Domain.Users;

/// <summary>Registro de auditoría de accesos (seguridad.BitacoraAccesos).</summary>
public sealed class AccessLog
{
    private AccessLog() { }

    public long Id { get; private set; }
    public string? UserName { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string Event { get; private set; } = default!;
    public bool Succeeded { get; private set; }
    public string? IpAddress { get; private set; }
    public string? DeviceId { get; private set; }
    public string? UserAgent { get; private set; }
    public string? Detail { get; private set; }

    public static AccessLog Create(string? userName, string @event, bool succeeded, DateTimeOffset now,
        string? ip, string? deviceId, string? userAgent, string? detail = null) => new()
    {
        UserName = userName?.Length > 20 ? userName[..20] : userName,
        Event = @event,
        Succeeded = succeeded,
        OccurredAtUtc = now,
        IpAddress = ip,
        DeviceId = deviceId,
        UserAgent = userAgent?.Length > 256 ? userAgent[..256] : userAgent,
        Detail = detail?.Length > 200 ? detail[..200] : detail
    };
}
