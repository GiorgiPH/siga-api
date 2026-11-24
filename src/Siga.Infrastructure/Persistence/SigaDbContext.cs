using Microsoft.EntityFrameworkCore;
using Siga.Application.Abstractions.Data;
using Siga.Domain.Users;

namespace Siga.Infrastructure.Persistence;

public sealed class SigaDbContext(DbContextOptions<SigaDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SigaDbContext).Assembly);
}
