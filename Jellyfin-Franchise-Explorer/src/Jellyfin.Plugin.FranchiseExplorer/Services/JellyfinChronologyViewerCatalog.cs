using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.FranchiseExplorer.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.FranchiseExplorer.Services;

/// <summary>
/// Permission-filtered read-only Jellyfin 12.2 library adapter.
/// QueryUser is ALWAYS non-null and ConfigureUserAccess is applied before enumeration.
/// Never use the global automation/recovery snapshot for viewer playback.
/// </summary>
public sealed class JellyfinChronologyViewerCatalog : IChronologyViewerCatalog
{
    private readonly Dictionary<Guid, ChronologyMediaItem> _items;
    private readonly Dictionary<Guid, BoxSet> _collections;
    private readonly User _viewer;

    public JellyfinChronologyViewerCatalog(ILibraryManager library, User viewer)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(viewer);
        _viewer = viewer;
        if (library.IsScanRunning)
            throw new ChronologyResolutionException("library_unavailable",
                "The Jellyfin library is being scanned. Try again after it finishes.");

        var query = new InternalItemsQuery(viewer)
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie, BaseItemKind.Series,
                BaseItemKind.Episode, BaseItemKind.BoxSet
            },
            Recursive = true,
            EnableTotalRecordCount = false,
            IsVirtualItem = false
        };
        library.ConfigureUserAccess(query, viewer);
        var scoped = library.GetItemList(query);
        if (library.IsScanRunning)
            throw new ChronologyResolutionException("library_unavailable",
                "The library changed while preparing the chronological preview.");
        if (scoped.Count > 250000)
            throw new ChronologyResolutionException("library_too_large",
                "The accessible media library exceeds the current resolver limit.");

        // The dictionary is intentionally constructed only from items returned
        // by the authenticated user's access-filtered query.
        _collections = scoped.OfType<BoxSet>().GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());
        var series = scoped.OfType<Series>().GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());
        _items = new Dictionary<Guid, ChronologyMediaItem>(scoped.Count);
        foreach (var item in scoped)
        {
            var episode = item as Episode;
            series.TryGetValue(episode?.SeriesId ?? Guid.Empty, out var parent);
            var kind = item switch
            {
                Movie => "Movie", Series => "Series",
                Episode => "Episode", BoxSet => "BoxSet", _ => string.Empty
            };
            if (kind.Length == 0) continue;
            // Do not return filesystem paths to the browser. A title can be
            // present but currently unavailable, in which case the resolver
            // will warn instead of pretending it can be played.
            var playable = item is Movie or Episode &&
                !item.IsVirtualItem && !string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path);
            _items[item.Id] = new ChronologyMediaItem
            {
                Id = item.Id, Kind = kind, Name = item.Name ?? string.Empty,
                SeriesId = episode?.SeriesId ?? Guid.Empty,
                Season = episode?.ParentIndexNumber,
                Episode = episode?.IndexNumber, EpisodeEnd = episode?.IndexNumberEnd,
                RunTimeTicks = item.RunTimeTicks ?? 0,
                IsPlayable = playable,
                ProviderIds = new Dictionary<string, string>(item.ProviderIds ?? new(),
                    StringComparer.OrdinalIgnoreCase),
                SeriesProviderIds = new Dictionary<string, string>(parent?.ProviderIds ?? new(),
                    StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    public Task<ChronologyMediaItem?> FindAsync(Guid itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items.TryGetValue(itemId, out var media);
        return Task.FromResult(media);
    }

    public Task<IReadOnlyList<ChronologyMediaItem>> EpisodesAsync(
        Guid seriesId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ChronologyMediaItem> episodes = _items.Values
            .Where(x => x.Kind == "Episode" && x.SeriesId == seriesId)
            .OrderBy(x => x.Season ?? int.MaxValue)
            .ThenBy(x => x.Episode ?? int.MaxValue)
            .ThenBy(x => x.Id)
            .ToArray();
        return Task.FromResult(episodes);
    }

    public Task<IReadOnlyList<ChronologyMediaItem>> CollectionMembersAsync(
        Guid collectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_collections.TryGetValue(collectionId, out var collection))
            return Task.FromResult<IReadOnlyList<ChronologyMediaItem>>([]);

        // Linked children are ordered by the native BoxSet. Require both its
        // user-scoped child query AND presence in the user's accessible media
        // dictionary, so linked items from hidden libraries are never leaked.
        var children = collection.GetChildren(_viewer, true, new InternalItemsQuery(_viewer)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = false,
            EnableTotalRecordCount = false,
            CollapseBoxSetItems = false
        });
        IReadOnlyList<ChronologyMediaItem> allowed = children
            .Where(x => _items.TryGetValue(x.Id, out var media) &&
                media.Kind is "Movie" or "Series")
            .Select(x => _items[x.Id]).DistinctBy(x => x.Id).ToArray();
        return Task.FromResult(allowed);
    }
}
