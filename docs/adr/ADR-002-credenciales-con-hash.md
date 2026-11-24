# ADR-002 — Credenciales de la app en un esquema propio `seguridad`, con hash

**Estado:** Aceptado

## Contexto
`dbo.Usuarios.Contraseña nvarchar(20)` indica contraseñas en **texto plano**. La API no debe depender de ellas más allá del primer acceso.

## Decisión — migración progresiva ("lazy migration")
1. Esquema `seguridad` con tablas propias de la API; **no se altera `dbo.Usuarios`**.
2. **Primer login:** se valida contra `dbo.Usuarios.Contraseña` en tiempo constante; si es correcta se genera un hash **PBKDF2 (ASP.NET Core Identity V3)** en `seguridad.UsuariosApp.PasswordHash`.
3. **Logins siguientes:** solo contra el hash.
4. **Reinicio:** un administrador pone `PasswordHash = NULL`; el siguiente login vuelve a usar la contraseña del escritorio.

## Consecuencias
- Tras el primer login, la contraseña de la app es independiente de la del escritorio (hasta reinicio o hasta el endpoint de cambio de contraseña, fase 2).
- El usuario SQL de la API solo tiene `SELECT` sobre 5 columnas de `dbo.Usuarios` (`database/001_seguridad_permisos.sql`).
- Nunca se registra la contraseña en logs ni se devuelve en respuestas.
- Recomendación fuerte (fuera de este proyecto): migrar el sistema de escritorio a contraseñas con hash.
