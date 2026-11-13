using MediaBrowser.Model.Plugins;

namespace Bergafin.Auth.Configuration;

/// <summary>
/// Configuração do plugin (Painel → Plugins → Bergafin Auth).
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Endereço público do Jellyfin, sem barra no fim. Ex.: https://jellyflix.exemplo.com</summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>Issuer do provider OIDC no Authentik. Ex.: https://auth.exemplo.com/application/o/jellyfin/</summary>
    public string Issuer { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Grupo do Authentik que pode entrar.</summary>
    public string UsersGroup { get; set; } = "jellyfin_users";

    /// <summary>Grupo do Authentik que vira admin no Jellyfin (também pode entrar).</summary>
    public string AdminsGroup { get; set; } = "jellyfin_admins";

    /// <summary>Contas novas veem todas as bibliotecas.</summary>
    public bool EnableAllFolders { get; set; } = true;

    /// <summary>
    /// Provedores de contas que já existem e podem ser ligadas ao Authentik pelo mesmo nome
    /// (migração do LDAP). Contas com o provedor padrão (senha local, como "sala" e o admin
    /// de emergência) nunca são ligadas.
    /// </summary>
    public string[] LinkableProviders { get; set; } =
    [
        "Jellyfin.Plugin.LDAP_Auth.LdapAuthenticationProviderPlugin",
    ];
}
