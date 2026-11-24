IF OBJECT_ID(N'[seguridad].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'seguridad') IS NULL EXEC(N'CREATE SCHEMA [seguridad];');
    CREATE TABLE [seguridad].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    IF SCHEMA_ID(N'seguridad') IS NULL EXEC(N'CREATE SCHEMA [seguridad];');
END;

IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    CREATE TABLE [seguridad].[BitacoraAccesos] (
        [Id] bigint NOT NULL IDENTITY,
        [Usuario] nvarchar(20) NULL,
        [FechaUtc] datetimeoffset NOT NULL,
        [Evento] varchar(40) NOT NULL,
        [Exitoso] bit NOT NULL,
        [Ip] varchar(45) NULL,
        [DispositivoId] nvarchar(100) NULL,
        [UserAgent] nvarchar(256) NULL,
        [Detalle] nvarchar(200) NULL,
        CONSTRAINT [PK_BitacoraAccesos] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    CREATE TABLE [seguridad].[RefreshTokens] (
        [Id] bigint NOT NULL IDENTITY,
        [Usuario] nvarchar(20) NOT NULL,
        [TokenHash] char(64) NOT NULL,
        [FamiliaId] uniqueidentifier NOT NULL,
        [DispositivoId] nvarchar(100) NULL,
        [FechaCreacionUtc] datetimeoffset NOT NULL,
        [ExpiraUtc] datetimeoffset NOT NULL,
        [RevocadoUtc] datetimeoffset NULL,
        [MotivoRevocacion] varchar(30) NULL,
        [ReemplazadoPorHash] char(64) NULL,
        [IpCreacion] varchar(45) NULL,
        [UserAgent] nvarchar(256) NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Usuarios] FOREIGN KEY ([Usuario]) REFERENCES [dbo].[Usuarios] ([Usuario]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    CREATE TABLE [seguridad].[UsuariosApp] (
        [Usuario] nvarchar(20) NOT NULL,
        [PasswordHash] nvarchar(256) NULL,
        [AccesoAppHabilitado] bit NOT NULL,
        [IntentosFallidos] int NOT NULL,
        [BloqueadoHastaUtc] datetimeoffset NULL,
        [UltimoAccesoUtc] datetimeoffset NULL,
        [PasswordActualizadoUtc] datetimeoffset NULL,
        [FechaCreacionUtc] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_UsuariosApp] PRIMARY KEY ([Usuario]),
        CONSTRAINT [FK_UsuariosApp_Usuarios] FOREIGN KEY ([Usuario]) REFERENCES [dbo].[Usuarios] ([Usuario]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    CREATE INDEX [IX_BitacoraAccesos_Usuario_FechaUtc] ON [seguridad].[BitacoraAccesos] ([Usuario], [FechaUtc] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_FamiliaId] ON [seguridad].[RefreshTokens] ([FamiliaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_Usuario] ON [seguridad].[RefreshTokens] ([Usuario]);
END;

IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RefreshTokens_TokenHash] ON [seguridad].[RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [seguridad].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116184030_InitialSecuritySchema'
)
BEGIN
    INSERT INTO [seguridad].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261116184030_InitialSecuritySchema', N'10.0.15');
END;

COMMIT;
GO

