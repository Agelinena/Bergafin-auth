using Bergafin.Auth.Oidc;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Bergafin.Auth.Api;

/// <summary>Resultado da checagem de quem voltou do Authentik.</summary>
public sealed record ProvisionResult(User? User, string? Denied);

/// <summary>
/// Cria ou liga a conta do Jellyfin de quem entrou pelo Authentik e aplica os grupos.
/// </summary>
public sealed class UserProvisioner
{
    private const string DefaultPasswordResetProvider = "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider";

    private readonly IUserManager _userManager;
    private readonly ILogger<UserProvisioner> _logger;

    public UserProvisioner(IUserManager userManager, ILogger<UserProvisioner> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<ProvisionResult> Provision(AuthentikIdentity identity)
    {
        var config = Plugin.Instance!.Configuration;
        var isAdmin = identity.Groups.Contains(config.AdminsGroup, StringComparer.OrdinalIgnoreCase);
        var isUser = isAdmin || identity.Groups.Contains(config.UsersGroup, StringComparer.OrdinalIgnoreCase);
        if (!isUser)
        {
            _logger.LogInformation("Authentik: {User} não está em {Group}; acesso negado.", identity.Username, config.UsersGroup);
            return new ProvisionResult(null, "Sua conta não tem acesso ao Jellyfin. Peça para entrar no grupo " + config.UsersGroup + ".");
        }

        var user = _userManager.GetUserByName(identity.Username);
        var isNew = false;
        if (user is null)
        {
            user = await _userManager.CreateUserAsync(identity.Username).ConfigureAwait(false);
            isNew = true;
            _logger.LogInformation("Authentik: conta {User} criada no Jellyfin.", identity.Username);
        }
        else if (!string.Equals(user.AuthenticationProviderId, AuthentikOnlyProvider.ProviderId, StringComparison.Ordinal)
                 && !config.LinkableProviders.Contains(user.AuthenticationProviderId, StringComparer.Ordinal))
        {
            // Conta local (senha do Jellyfin): "sala", admin de emergência ou outra criada à mão.
            _logger.LogWarning(
                "Authentik: {User} tem conta local no Jellyfin (provedor {Provider}); login recusado.",
                identity.Username,
                user.AuthenticationProviderId);
            return new ProvisionResult(null, "Já existe uma conta local com o nome " + identity.Username + ". Ela não pode entrar pelo Authentik.");
        }

        if (user.HasPermission(PermissionKind.IsDisabled))
        {
            return new ProvisionResult(null, "Esta conta está desativada no Jellyfin.");
        }

        var policy = _userManager.GetUserDto(user).Policy;
        policy.AuthenticationProviderId = AuthentikOnlyProvider.ProviderId;
        policy.PasswordResetProviderId = DefaultPasswordResetProvider;
        policy.IsAdministrator = isAdmin;
        policy.EnableRemoteAccess = true;
        if (isNew)
        {
            policy.EnableAllFolders = config.EnableAllFolders;
        }

        await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
        return new ProvisionResult(_userManager.GetUserById(user.Id), null);
    }
}
