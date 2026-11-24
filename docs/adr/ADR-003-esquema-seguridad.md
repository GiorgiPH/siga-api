# ADR-003 — Tablas nuevas del esquema `seguridad`

**Estado:** Aceptado

| Tabla | Propósito |
|---|---|
| `seguridad.UsuariosApp` | Credencial con hash, habilitación de acceso a la app, intentos fallidos, bloqueo, último acceso. 1:1 con `dbo.Usuarios`. |
| `seguridad.RefreshTokens` | Refresh tokens **hasheados (SHA-256)**, con rotación y detección de reuso por familia (sesión/dispositivo). |
| `seguridad.BitacoraAccesos` | Auditoría de login, refresh y logout (exitosos y fallidos). Sin FK para registrar también usuarios inexistentes. |

Los objetos de BD se nombran **en español** (consistencia con la BD existente); el código C# va **en inglés** y se mapea con `HasColumnName`.

El historial de migraciones de EF vive en `seguridad.__EFMigrationsHistory` para no tocar `dbo`.
