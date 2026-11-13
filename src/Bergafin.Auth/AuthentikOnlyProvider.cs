using System.Security.Authentication;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Authentication;

namespace Bergafin.Auth;

/// <summary>
/// Provedor das contas do Authentik: recusa qualquer login por senha.
/// Essas contas só ganham sessão pelo fluxo OIDC do plugin ou por Quick Connect.
/// </summary>
public class AuthentikOnlyProvider : IAuthenticationProvider
{
    /// <summary>Id gravado em User.AuthenticationProviderId.</summary>
    public static readonly string ProviderId = typeof(AuthentikOnlyProvider).FullName!;

    public string Name => "Authentik (Bergafin)";

    public bool IsEnabled => true;

    public Task<ProviderAuthenticationResult> Authenticate(string username, string password)
        => throw new AuthenticationException("Esta conta entra só pelo Authentik.");

    public Task ChangePassword(User user, string newPassword)
        => throw new NotSupportedException("A senha desta conta é a do Authentik.");
}
