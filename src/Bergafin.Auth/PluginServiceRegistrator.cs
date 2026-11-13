using Bergafin.Auth.Api;
using Bergafin.Auth.Oidc;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Bergafin.Auth;

/// <summary>Registra os serviços do plugin no Jellyfin.</summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IAuthenticationProvider, AuthentikOnlyProvider>();
        serviceCollection.AddSingleton<LoginStore>();
        serviceCollection.AddSingleton<AuthentikClient>();
        serviceCollection.AddSingleton<UserProvisioner>();
    }
}
