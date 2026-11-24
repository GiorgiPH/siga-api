# ADR-001 — Usar los usuarios existentes de `dbo.Usuarios`, sin duplicarlos

**Estado:** Aceptado

## Contexto
SIGA (sistema de escritorio) ya administra sus usuarios en `dbo.Usuarios` de la BD `Administracion`.

## Decisión
La identidad del usuario (quién es, nombre, tipo, si está activo) **se lee de `dbo.Usuarios`**, que es la única fuente de verdad. La API la mapea como entidad de solo lectura (`SystemUser`), excluida de migraciones, y solo con las columnas `Usuario`, `Contraseña`, `Nombre`, `TipoUsuario` y `Estatus`.

## Consecuencias
- Si el sistema de escritorio da de baja o inactiva a un usuario, automáticamente pierde acceso a la app.
- Se evitan altas/bajas desincronizadas y doble administración.
- La API **nunca escribe** en `dbo.Usuarios`.
