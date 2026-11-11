using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.FranchiseExplorer.Models;

/// <summary>
/// Step 5 viewer-permission-scoped media metadata. No file paths, credentials,
/// admin-entered labels, or playback URLs may appear in this DTO.
/// </summary>
public sealed class ChronologyMediaItem
{
    public Guid Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid SeriesId { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public int? EpisodeEnd { get; init; }
    public long RunTimeTicks { get; init; }
    public bool IsPlayable { get; init; } = true;
    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> SeriesProviderIds { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Implementations MUST return only items visible to the authenticated viewer.
/// Never pass a global/admin media snapshot to the resolver.
/// </summary>
public interface IChronologyViewerCatalog
{
    Task<ChronologyMediaItem?> FindAsync(Guid itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChronologyMediaItem>> EpisodesAsync(Guid seriesId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChronologyMediaItem>> CollectionMembersAsync(Guid collectionId, CancellationToken cancellationToken);
}

/// <summary>Resolved occurrence is explicitly distinguished from the Jellyfin item ID.</summary>
public sealed class ChronologyOccurrence
{
    public Guid OccurrenceId { get; init; }
    public Guid SlotId { get; init; }
    public Guid ReferenceId { get; init; }
    public Guid JellyfinItemId { get; init; }
    public Guid SourceItemId { get; init; }
    public string LogicalMediaKey { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Position { get; init; }
    public int Season { get; init; }
    public int Episode { get; init; }
    public int EpisodeEnd { get; init; }
    public long RunTimeTicks { get; init; }
}

/// <summary>Non-fatal exclusions or unresolved/ambiguous references are never hidden.</summary>
public sealed record ChronologyWarning(string Code, string ReferenceId, string Message);

public sealed class ChronologyResolution
{
    public int SchemaVersion { get; init; } = 1;
    public string Mode { get; init; } = "chronological";
    public bool IsCanonicalTracking { get; init; } = false; // Step 8 enables verified tracking.
    public bool PreviewOnly { get; init; } = true;
    public Guid FranchiseId { get; init; }
    public Guid OrderId { get; init; }
    public long OrderRevision { get; init; }
    public Guid QueueSnapshotId { get; init; }
    public IReadOnlyList<ChronologyOccurrence> Items { get; init; } = Array.Empty<ChronologyOccurrence>();
    public IReadOnlyList<ChronologyWarning> Warnings { get; init; } = Array.Empty<ChronologyWarning>();
}

/// <summary>Typed validation failure, safe to return without exposing media to viewers.</summary>
public sealed class ChronologyResolutionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
