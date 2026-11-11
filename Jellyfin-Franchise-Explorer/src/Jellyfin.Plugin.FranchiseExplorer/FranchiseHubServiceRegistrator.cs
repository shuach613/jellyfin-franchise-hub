using System;
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
        // Lazy initialization: plugin instance/data folder is available when a service
        // is resolved; the original legacy player remains completely independent.
        serviceCollection.AddSingleton(_ => new FranchiseHubStateStore(
            Plugin.Instance?.DataFolderPath ?? throw new InvalidOperationException("Franchise Hub data folder unavailable.")));
        serviceCollection.AddSingleton(provider => new LegacyTimelineMigrationService(
            Plugin.Instance?.DataFolderPath ?? throw new InvalidOperationException("Franchise Hub data folder unavailable."),
            provider.GetRequiredService<FranchiseHubStateStore>()));
    }
}
