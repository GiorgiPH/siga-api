-- =====================================================================
-- SIGA Web API · Marcar la migración InitialSecuritySchema como aplicada
--
-- Usar SOLO si el esquema [seguridad] (UsuariosApp, RefreshTokens, BitacoraAccesos)
-- ya fue creado manualmente. Así EF Core no intentará crearlo de nuevo y las
-- migraciones futuras se aplicarán normalmente.
--
-- Paso 1: revisar que las columnas coincidan con database/InitialSecuritySchema.sql
-- Paso 2: ejecutar el bloque de inserción.
-- =====================================================================
USE [Administracion];
GO

-- ---------- Paso 1: verificación (revisar el resultado antes de continuar) ----------
SELECT  t.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM    INFORMATION_SCHEMA.TABLES t
JOIN    INFORMATION_SCHEMA.COLUMNS c
        ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME
WHERE   t.TABLE_SCHEMA = 'seguridad'
ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION;

SELECT  name AS indice, OBJECT_NAME(object_id) AS tabla
FROM    sys.indexes
WHERE   OBJECT_SCHEMA_NAME(object_id) = 'seguridad' AND name IS NOT NULL;
GO

-- ---------- Paso 2: registrar la migración como aplicada ----------
IF OBJECT_ID(N'[seguridad].[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [seguridad].[__EFMigrationsHistory] (
        [MigrationId]    nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32)  NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;

IF NOT EXISTS (SELECT 1 FROM [seguridad].[__EFMigrationsHistory]
               WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema')
BEGIN
    INSERT INTO [seguridad].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261116184030_InitialSecuritySchema', N'10.0.15');
END;
GO

SELECT * FROM [seguridad].[__EFMigrationsHistory];
