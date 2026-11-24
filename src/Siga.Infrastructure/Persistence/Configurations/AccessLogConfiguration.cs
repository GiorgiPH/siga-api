using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siga.Domain.Users;

namespace Siga.Infrastructure.Persistence.Configurations;

internal sealed class AccessLogConfiguration : IEntityTypeConfiguration<AccessLog>
{
    public void Configure(EntityTypeBuilder<AccessLog> builder)
    {
        builder.ToTable("BitacoraAccesos", "seguridad");
        builder.HasKey(l => l.Id).HasName("PK_BitacoraAccesos");

        // Sin FK a dbo.Usuarios: también registra intentos con usuarios inexistentes
        builder.Property(l => l.UserName).HasColumnName("Usuario").HasMaxLength(20);
        builder.Property(l => l.OccurredAtUtc).HasColumnName("FechaUtc");
        builder.Property(l => l.Event).HasColumnName("Evento").HasMaxLength(40).IsUnicode(false);
        builder.Property(l => l.Succeeded).HasColumnName("Exitoso");
        builder.Property(l => l.IpAddress).HasColumnName("Ip").HasMaxLength(45).IsUnicode(false);
        builder.Property(l => l.DeviceId).HasColumnName("DispositivoId").HasMaxLength(100);
        builder.Property(l => l.UserAgent).HasMaxLength(256);
        builder.Property(l => l.Detail).HasColumnName("Detalle").HasMaxLength(200);

        builder.HasIndex(l => new { l.UserName, l.OccurredAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_BitacoraAccesos_Usuario_FechaUtc");
    }
}
