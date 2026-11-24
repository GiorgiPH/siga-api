using Microsoft.EntityFrameworkCore;
using Siga.Domain.Users;

namespace Siga.Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<SystemUser> SystemUsers { get; }
    DbSet<AppUser> AppUsers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AccessLog> AccessLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
