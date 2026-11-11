using Jellyfin.Plugin.FranchiseExplorer.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.FranchiseExplorer;

/// <summary>Registers the new pure server resolver without affecting the legacy player.</summary>
public sealed class FranchiseHubServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IWatchOrderResolver, ChronologicalWatchOrderResolver>();
    }
}
