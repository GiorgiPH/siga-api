using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Siga.Application.Abstractions.Data;
using Siga.Domain.Users;

namespace Siga.Application.UnitTests.TestSupport;

/// <summary>DbContext en memoria para probar handlers sin SQL Server.</summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

    public static TestDbContext Create() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemUser>().HasKey(u => u.UserName);
        modelBuilder.Entity<AppUser>().HasKey(a => a.UserName);
        modelBuilder.Entity<AppUser>().Ignore(a => a.RowVersion);
        modelBuilder.Entity<RefreshToken>().HasKey(t => t.Id);
        modelBuilder.Entity<AccessLog>().HasKey(l => l.Id);
    }

    /// <summary>Inserta un usuario en la "tabla legada" (la entidad no tiene constructor público).</summary>
    public void SeedSystemUser(string userName, string? password, string? status = "ACTIVO",
        string? name = "Juan Pérez", string? userType = "VENDEDOR")
    {
        var user = (SystemUser)Activator.CreateInstance(typeof(SystemUser), nonPublic: true)!;
        Set(user, nameof(SystemUser.UserName), userName);
        Set(user, nameof(SystemUser.LegacyPassword), password);
        Set(user, nameof(SystemUser.Name), name);
        Set(user, nameof(SystemUser.UserType), userType);
        Set(user, nameof(SystemUser.Status), status);
        SystemUsers.Add(user);
        SaveChanges();
        ChangeTracker.Clear();
    }

    private static void Set(object target, string property, object? value) =>
        typeof(SystemUser).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(target, value);
}
