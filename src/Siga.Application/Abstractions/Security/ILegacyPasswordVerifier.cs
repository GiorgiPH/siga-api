namespace Siga.Application.Abstractions.Security;

public interface ILegacyPasswordVerifier
{
    bool Verify(string? storedPlainPassword, string providedPassword);
}
