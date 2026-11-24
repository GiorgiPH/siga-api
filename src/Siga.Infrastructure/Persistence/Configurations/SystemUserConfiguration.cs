using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siga.Domain.Users;

namespace Siga.Infrastructure.Persistence.Configurations;

internal sealed class SystemUserConfiguration : IEntityTypeConfiguration<SystemUser>
{
    public void Configure(EntityTypeBuilder<SystemUser> builder)
    {
        // Tabla del sistema de escritorio: NUNCA se crea ni modifica por migraciones
        builder.ToTable("Usuarios", "dbo", t => t.ExcludeFromMigrations());
        builder.HasKey(u => u.UserName);

        builder.Property(u => u.UserName).HasColumnName("Usuario").HasMaxLength(20);
        builder.Property(u => u.LegacyPassword).HasColumnName("Contraseña").HasMaxLength(20);
        builder.Property(u => u.Name).HasColumnName("Nombre").HasMaxLength(50);
        builder.Property(u => u.UserType).HasColumnName("TipoUsuario").HasMaxLength(20).IsUnicode(false);
        builder.Property(u => u.Status).HasColumnName("Estatus").HasMaxLength(10).IsUnicode(false);
        // Foto, Nota y FechaAlta NO se mapean a propósito.
    }
}
