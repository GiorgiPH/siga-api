using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siga.Domain.Users;

namespace Siga.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("UsuariosApp", "seguridad");
        builder.HasKey(a => a.UserName).HasName("PK_UsuariosApp");

        builder.Property(a => a.UserName).HasColumnName("Usuario").HasMaxLength(20);
        builder.Property(a => a.PasswordHash).HasColumnName("PasswordHash").HasMaxLength(256);
        builder.Property(a => a.AppAccessEnabled).HasColumnName("AccesoAppHabilitado");
        builder.Property(a => a.FailedAttempts).HasColumnName("IntentosFallidos");
        builder.Property(a => a.LockoutEndUtc).HasColumnName("BloqueadoHastaUtc");
        builder.Property(a => a.LastLoginUtc).HasColumnName("UltimoAccesoUtc");
        builder.Property(a => a.PasswordChangedUtc).HasColumnName("PasswordActualizadoUtc");
        builder.Property(a => a.CreatedAtUtc).HasColumnName("FechaCreacionUtc");
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.Ignore(a => a.HasPassword);

        builder.HasOne<SystemUser>().WithOne()
            .HasForeignKey<AppUser>(a => a.UserName)
            .HasConstraintName("FK_UsuariosApp_Usuarios")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
