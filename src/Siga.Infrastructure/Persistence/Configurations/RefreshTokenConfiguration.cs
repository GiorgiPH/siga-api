using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siga.Domain.Users;

namespace Siga.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", "seguridad");
        builder.HasKey(t => t.Id).HasName("PK_RefreshTokens");

        builder.Property(t => t.UserName).HasColumnName("Usuario").HasMaxLength(20);
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(t => t.FamilyId).HasColumnName("FamiliaId");
        builder.Property(t => t.DeviceId).HasColumnName("DispositivoId").HasMaxLength(100);
        builder.Property(t => t.CreatedAtUtc).HasColumnName("FechaCreacionUtc");
        builder.Property(t => t.ExpiresAtUtc).HasColumnName("ExpiraUtc");
        builder.Property(t => t.RevokedAtUtc).HasColumnName("RevocadoUtc");
        builder.Property(t => t.RevocationReason).HasColumnName("MotivoRevocacion").HasMaxLength(30).IsUnicode(false);
        builder.Property(t => t.ReplacedByTokenHash).HasColumnName("ReemplazadoPorHash").HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(t => t.CreatedByIp).HasColumnName("IpCreacion").HasMaxLength(45).IsUnicode(false);
        builder.Property(t => t.UserAgent).HasMaxLength(256);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_TokenHash");
        builder.HasIndex(t => t.UserName).HasDatabaseName("IX_RefreshTokens_Usuario");
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("IX_RefreshTokens_FamiliaId");

        builder.HasOne<SystemUser>().WithMany()
            .HasForeignKey(t => t.UserName)
            .HasConstraintName("FK_RefreshTokens_Usuarios")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
