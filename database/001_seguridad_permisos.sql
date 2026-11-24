-- =====================================================================
-- SIGA Web API · Usuarios SQL y permisos (principio de mínimo privilegio)
-- Ejecutar con un usuario administrador.
-- Reemplazar <<CAMBIAR>> por contraseñas seguras del gestor de secretos.
-- =====================================================================
USE [master];
CREATE LOGIN siga_api      WITH PASSWORD = N'<<CAMBIAR>>', CHECK_POLICY = ON;
CREATE LOGIN siga_migrator WITH PASSWORD = N'<<CAMBIAR>>', CHECK_POLICY = ON;
GO

USE [Administracion];
CREATE USER siga_api      FOR LOGIN siga_api;
CREATE USER siga_migrator FOR LOGIN siga_migrator;
GO

-- API: solo lectura de las columnas necesarias de la tabla legada
GRANT SELECT ON dbo.Usuarios (Usuario, [Contraseña], Nombre, TipoUsuario, Estatus) TO siga_api;

-- API: lectura/escritura únicamente en el esquema propio (el esquema seguridad ya debe existir)
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::seguridad TO siga_api;
GO

-- Migrador: crear objetos y referenciar dbo.Usuarios con FK.
-- El historial de migraciones vive en seguridad.__EFMigrationsHistory, por lo que NO necesita permisos sobre dbo.
ALTER ROLE db_ddladmin ADD MEMBER siga_migrator;
GRANT REFERENCES ON dbo.Usuarios TO siga_migrator;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::seguridad TO siga_migrator;
GO
