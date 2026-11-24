namespace Siga.Application.Abstractions.Security;

public enum PasswordCheck { Failed, Success, SuccessRehashNeeded }

public interface IPasswordHasher
{
    string Hash(string password);
    PasswordCheck Verify(string passwordHash, string providedPassword);
    /// <summary>Ejecuta una verificación ficticia para igualar tiempos cuando el usuario no existe.</summary>
    void VerifyDummy(string providedPassword);
}
