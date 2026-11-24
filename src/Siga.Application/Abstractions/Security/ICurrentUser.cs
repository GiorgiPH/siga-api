namespace Siga.Application.Abstractions.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Usuario autenticado. Lanza <see cref="InvalidOperationException"/> si no hay sesión.</summary>
    string UserName { get; }
}
