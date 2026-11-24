# ADR-005 — JWT corto + refresh token rotativo

**Estado:** Aceptado

- Access token JWT HS256 de **15 minutos** (`Jwt:AccessTokenMinutes`), llave ≥ 256 bits fuera del repositorio, `ValidAlgorithms` fijo y `ClockSkew` de 30 s.
- Refresh token opaco aleatorio de 64 bytes (86 caracteres base64url), **14 días** (`Auth:RefreshTokenDays`), guardado como hash SHA-256 y **rotado en cada uso**.
- Si se presenta un refresh token ya revocado (posible robo), **se revoca toda la familia** (todas las sesiones derivadas de ese login).

## Consecuencias
- Dos refresh simultáneos con el mismo token cierran la sesión: la app móvil debe serializar el refresh (p. ej. con `SemaphoreSlim`).
- Tras logout, el access token sigue siendo válido hasta expirar (comportamiento esperado de JWT).
