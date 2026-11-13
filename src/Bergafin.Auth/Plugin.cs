using Bergafin.Auth.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Bergafin.Auth;

/// <summary>
/// Login no Jellyfin só pelo Authentik (OIDC), com Quick Connect aprovado pelo Authentik.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>GUID fixo do plugin; não mude, o Jellyfin identifica o plugin por ele.</summary>
    public static readonly Guid PluginId = Guid.Parse("6f1b7c1e-8a4e-4c2b-9b7a-2f3d5e9a1b40");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Bergafin Auth";

    public override Guid Id => PluginId;

    public override string Description => "Login só pelo Authentik (OIDC) e Quick Connect aprovado pelo Authentik.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
        };
    }
}
