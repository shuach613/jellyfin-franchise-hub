using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FranchiseExplorer.Configuration;
using Jellyfin.Plugin.FranchiseExplorer.Models;

namespace Jellyfin.Plugin.FranchiseExplorer.Services;

/// <summary>Authoritative, side-effect-free chronological expansion contract.</summary>
public interface IWatchOrderResolver
{
    Task<ChronologyResolution> ResolveAsync(
        PluginConfiguration config, Guid franchiseId, Guid authenticatedUserId,
        IChronologyViewerCatalog viewerCatalog, CancellationToken cancellationToken = default);
}

/// <summary>
/// Step 5 server-side queue builder. No playback, progress writes, native watched
/// flags, filesystem writes or browser state. Occurrence keys are deterministic
/// provisional IDs until the durable identity registry is installed in Step 6.
/// </summary>
public sealed class ChronologicalWatchOrderResolver : IWatchOrderResolver
{
    public const int MaxReferences = 2000;
    public const int MaxItems = 25000;
    private const string IdentityPrefix = "franchise-hub/occurrence/v1/";

    private sealed record Reference(
        Guid Id, Guid ItemId, string RangeMode, int StartSeason, int EndSeason,
        int StartEpisode, int EndEpisode, int SourceOrdinal);

    public async Task<ChronologyResolution> ResolveAsync(
        PluginConfiguration config, Guid franchiseId, Guid authenticatedUserId,
        IChronologyViewerCatalog viewerCatalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(viewerCatalog);
        if (franchiseId == Guid.Empty || authenticatedUserId == Guid.Empty)
            throw new ChronologyResolutionException("invalid_request", "A franchise and authenticated viewer are required.");

        var nodes = config.Nodes ?? [];
        var franchise = nodes.FirstOrDefault(n => Parse(n.Id) == franchiseId &&
            string.Equals(n.NodeType, "Franchise", StringComparison.OrdinalIgnoreCase));
        if (franchise is null)
            throw new ChronologyResolutionException("not_found", "The requested franchise does not exist.");

        bool InFranchise(string owner)
        {
            var visited = new HashSet<Guid>();
            var cursor = Parse(owner);
            while (cursor != Guid.Empty && visited.Add(cursor))
            {
                if (cursor == franchiseId) return true;
                var node = nodes.FirstOrDefault(n => Parse(n.Id) == cursor);
                if (node is null || string.Equals(node.NodeType, "Franchise", StringComparison.OrdinalIgnoreCase))
                    return false; // A nested, independent franchise cannot leak into this one.
                cursor = Parse(node.ParentId);
            }
            return false;
        }

        var candidates = (config.Rows ?? []).Where(r =>
            string.Equals(r.RowType, "Timeline", StringComparison.OrdinalIgnoreCase) &&
            InFranchise(r.ParentNodeId)).ToArray();
        FranchiseRow row;
        var primary = Parse(franchise.PrimaryChronologyRowId);
        if (primary != Guid.Empty)
        {
            row = candidates.FirstOrDefault(r => Parse(r.Id) == primary)
                ?? throw new ChronologyResolutionException("invalid_primary_order",
                    "The designated chronological order is not valid for this franchise.");
        }
        else if (candidates.Length == 1) row = candidates[0];
        else if (candidates.Length == 0)
            throw new ChronologyResolutionException("chronology_not_configured",
                "Configure a chronological Timeline row before playback.");
        else
            throw new ChronologyResolutionException("ambiguous_primary_order",
                "Choose the franchise's primary chronological Timeline row in settings.");

        var orderId = Parse(row.Id);
        if (orderId == Guid.Empty)
            throw new ChronologyResolutionException("invalid_primary_order", "The chronological row ID is invalid.");

        var warnings = new List<ChronologyWarning>();
        var refs = new List<Reference>();
        var usedRefs = new HashSet<Guid>();
        var links = (config.MediaLinks ?? []).Where(l => Parse(l.Id) != Guid.Empty)
            .GroupBy(l => Parse(l.Id)).ToDictionary(g => g.Key, g => g.First());

        void Add(string? rawId, string? rawItemId, string? mode,
            int startSeason, int endSeason, int startEp, int endEp)
        {
            var id = Parse(rawId);
            if (id == Guid.Empty)
            {
                warnings.Add(new("invalid_reference", string.Empty, "An entry has an invalid reference ID."));
                return;
            }
            if (!usedRefs.Add(id))
            {
                warnings.Add(new("duplicate_slot_reference", id.ToString("D"),
                    "The same reference was listed twice; add a distinct entry for intentional repeat viewing."));
                return;
            }
            refs.Add(new(id, Parse(rawItemId), mode ?? "Full",
                startSeason, endSeason, startEp, endEp, refs.Count));
        }

        foreach (var linkId in row.MediaLinkIds ?? [])
        {
            var id = Parse(linkId);
            if (id != Guid.Empty && links.TryGetValue(id, out var link))
                Add(link.Id, link.JellyfinItemId, link.RangeMode, link.StartSeason, link.EndSeason,
                    link.StartEpisode, link.EndEpisode);
            else
                warnings.Add(new("missing_reference", linkId ?? "",
                    "A row reference is missing from the franchise configuration."));
        }

        foreach (var entry in row.Items ?? [])
            Add(entry.Id, entry.JellyfinItemId, entry.RangeMode, entry.StartSeason, entry.EndSeason,
                entry.StartEpisode, entry.EndEpisode);

        if (refs.Count > MaxReferences)
            throw new ChronologyResolutionException("too_many_references", "This timeline exceeds the maximum reference count.");
        if ((row.NodeIds?.Count ?? 0) > 0)
            warnings.Add(new("unexpanded_groups", row.Id,
                "Nested groups are not implicitly expanded; place their media explicitly in the timeline."));

        var order = new Dictionary<Guid, int>();
        foreach (var raw in row.MediaOrder ?? [])
        {
            var id = Parse(raw);
            if (id != Guid.Empty && !order.TryAdd(id, order.Count))
                warnings.Add(new("duplicate_media_order", raw, "Duplicate MediaOrder entries are ignored."));
        }
        foreach (var id in order.Keys)
        {
            if (!usedRefs.Contains(id))
                warnings.Add(new("orphan_media_order", id.ToString("D"),
                    "The order mentions a reference that does not belong to this row."));
        }
        // Equal rank entries retain original row ordering, never IMDb or file release order.
        refs.Sort((a, b) =>
        {
            var aRank = order.TryGetValue(a.Id, out var x) ? x : int.MaxValue;
            var bRank = order.TryGetValue(b.Id, out var y) ? y : int.MaxValue;
            return aRank != bRank ? aRank.CompareTo(bRank) : a.SourceOrdinal.CompareTo(b.SourceOrdinal);
        });

        var exclusions = EffectiveExclusions(config, franchiseId, row);
        var output = new List<ChronologyOccurrence>();
        foreach (var reference in refs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.ItemId == Guid.Empty)
            {
                warnings.Add(new("invalid_media_id", reference.Id.ToString("D"),
                    "A reference does not identify a valid Jellyfin item."));
                continue;
            }
            var parent = await viewerCatalog.FindAsync(reference.ItemId, cancellationToken).ConfigureAwait(false);
            if (parent is null)
            {
                warnings.Add(new("missing_or_forbidden", reference.Id.ToString("D"),
                    "The referenced media is missing or inaccessible to this viewer."));
                continue;
            }

            var expanded = new List<(ChronologyMediaItem Item, ChronologyMediaItem? Parent)>();
            switch (parent.Kind)
            {
                case "Movie":
                case "Episode":
                    expanded.Add((parent, null));
                    break;
                case "Series":
                    await AddEpisodes(parent, reference, viewerCatalog, expanded, cancellationToken).ConfigureAwait(false);
                    break;
                case "BoxSet":
                    var children = await viewerCatalog.CollectionMembersAsync(parent.Id, cancellationToken).ConfigureAwait(false);
                    foreach (var child in children)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (child.Kind == "Movie") expanded.Add((child, null));
                        else if (child.Kind == "Series")
                            await AddEpisodes(child, reference with { RangeMode = "Full" }, viewerCatalog,
                                expanded, cancellationToken).ConfigureAwait(false);
                        else warnings.Add(new("unsupported_collection_member", reference.Id.ToString("D"),
                            "A collection member is not a movie or supported series."));
                    }
                    break;
                default:
                    warnings.Add(new("unsupported_type", reference.Id.ToString("D"),
                        "This media type cannot be used for chronological video playback."));
                    continue;
            }

            if (expanded.Count == 0)
                warnings.Add(new("empty_or_unavailable_reference", reference.Id.ToString("D"),
                    "No accessible playable media matched the reference and episode range."));

            var withinSlot = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (media, series) in expanded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (media.Id == Guid.Empty || media.Kind is not ("Movie" or "Episode") ||
                    !media.IsPlayable || Excluded(media, series, exclusions))
                {
                    if (media.Id != Guid.Empty && !media.IsPlayable)
                        warnings.Add(new("unavailable_media", reference.Id.ToString("D"),
                            "A referenced title is not currently playable."));
                    continue;
                }
                var logical = LogicalMediaKey(media, series);
                if (logical.StartsWith("jellyfin:", StringComparison.Ordinal))
                    warnings.Add(new("provisional_media_identity", reference.Id.ToString("D"),
                        "This item lacks stable provider identity; remapping it after a library scan will require review."));
                withinSlot.TryGetValue(logical, out var ordinal);
                withinSlot[logical] = ordinal + 1;
                var occurrenceId = StableGuid($"{IdentityPrefix}{orderId:D}/{reference.Id:D}/{logical}/{ordinal}");
                output.Add(new ChronologyOccurrence
                {
                    OccurrenceId = occurrenceId, SlotId = reference.Id, ReferenceId = reference.Id,
                    JellyfinItemId = media.Id, SourceItemId = parent.Id, LogicalMediaKey = logical,
                    Kind = media.Kind, Name = media.Name, Position = output.Count,
                    Season = media.Season ?? 0, Episode = media.Episode ?? 0,
                    EpisodeEnd = media.EpisodeEnd ?? media.Episode ?? 0,
                    RunTimeTicks = Math.Max(0, media.RunTimeTicks)
                });
                if (output.Count > MaxItems)
                    throw new ChronologyResolutionException("too_many_items",
                        "The resolved chronological queue exceeds the maximum item count.");
            }
        }

        var fingerprint = string.Join("|", output.Select(x =>
            $"{x.OccurrenceId:D}:{x.JellyfinItemId:D}:{x.Position}"));
        // Preview-only deterministic token: Step 6 will persist immutable snapshots
        // and durable occurrence IDs. Do NOT use this token for actual playback.
        var snapshotId = StableGuid($"franchise-hub/preview/v1/{authenticatedUserId:D}/{orderId:D}/" +
            $"{config.ConfigurationRevision}/{fingerprint}");
        return new ChronologyResolution
        {
            FranchiseId = franchiseId, OrderId = orderId,
            OrderRevision = config.ConfigurationRevision, QueueSnapshotId = snapshotId,
            Items = output.ToArray(), Warnings = warnings.ToArray()
        };
    }

    private static async Task AddEpisodes(
        ChronologyMediaItem series, Reference reference, IChronologyViewerCatalog catalog,
        List<(ChronologyMediaItem Item, ChronologyMediaItem? Parent)> output,
        CancellationToken cancellationToken)
    {
        var episodes = await catalog.EpisodesAsync(series.Id, cancellationToken).ConfigureAwait(false);
        foreach (var ep in episodes.Where(e => e.Kind == "Episode" &&
            e.SeriesId == series.Id && InRange(e, reference))
            .OrderBy(e => e.Season ?? int.MaxValue)
            .ThenBy(e => e.Episode ?? int.MaxValue)
            .ThenBy(e => e.EpisodeEnd ?? e.Episode ?? int.MaxValue)
            .ThenBy(e => e.Id))
        {
            output.Add((ep, series));
            if (output.Count > MaxItems)
                throw new ChronologyResolutionException("too_many_items", "A single series exceeds the queue limit.");
        }
    }

    private static bool InRange(ChronologyMediaItem ep, Reference reference)
    {
        if (ep.Episode is not > 0 || ep.Season is null) return false;
        var season = ep.Season.Value;
        var first = ep.Episode.Value;
        var last = Math.Max(first, ep.EpisodeEnd ?? first);
        var mode = reference.RangeMode;
        if (string.Equals(mode, "Full", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(mode))
            return season > 0;
        if (string.Equals(mode, "Seasons", StringComparison.OrdinalIgnoreCase))
            return reference.EndSeason >= reference.StartSeason &&
                season >= reference.StartSeason && season <= reference.EndSeason;
        if (!string.Equals(mode, "Episodes", StringComparison.OrdinalIgnoreCase) ||
            reference.StartEpisode < 1 || reference.EndEpisode < 1 ||
            reference.StartSeason > reference.EndSeason ||
            reference.StartSeason == reference.EndSeason && reference.StartEpisode > reference.EndEpisode)
            return false;
        // A multipart file covering any selected episode remains one indivisible playable item.
        return season >= reference.StartSeason && season <= reference.EndSeason &&
            (season > reference.StartSeason || last >= reference.StartEpisode) &&
            (season < reference.EndSeason || first <= reference.EndEpisode);
    }

    private static IReadOnlyList<AutoFranchiseExclusion> EffectiveExclusions(
        PluginConfiguration config, Guid franchiseId, FranchiseRow row)
    {
        var owners = new HashSet<Guid> { franchiseId };
        var cursor = Parse(row.ParentNodeId);
        while (cursor != Guid.Empty && owners.Add(cursor))
            cursor = Parse((config.Nodes ?? []).FirstOrDefault(n => Parse(n.Id) == cursor)?.ParentId);
        var relevantRegistrations = (config.AutoFranchises?.Registry ?? []).Where(r =>
            owners.Contains(Parse(r.FranchiseNodeId)) ||
            (r.Rows ?? []).Any(mapping => Parse(mapping.RowId) == Parse(row.Id)))
            .Select(r => Parse(r.Id)).ToHashSet();
        return (config.AutoFranchises?.Exclusions ?? []).Where(x => x.Enabled &&
            (Parse(x.RowId) == Guid.Empty || Parse(x.RowId) == Parse(row.Id)) &&
            (Parse(x.RegistrationId) != Guid.Empty
                ? relevantRegistrations.Contains(Parse(x.RegistrationId))
                : owners.Contains(Parse(x.FranchiseNodeId)))).ToArray();
    }

    private static bool Excluded(ChronologyMediaItem media, ChronologyMediaItem? parent,
        IReadOnlyList<AutoFranchiseExclusion> exclusions)
    {
        var isEpisode = media.Kind == "Episode";
        foreach (var x in exclusions)
        {
            var scope = (x.Scope ?? "").ToLowerInvariant();
            bool Matches(ChronologyMediaItem? candidate)
            {
                if (candidate is null) return false;
                var wantedId = Parse(x.JellyfinItemId);
                if (wantedId != Guid.Empty && wantedId == candidate.Id) return true;
                return !string.IsNullOrWhiteSpace(x.ProviderId) &&
                    candidate.ProviderIds.TryGetValue(x.ProviderName ?? "Imdb", out var actual) &&
                    string.Equals(actual, x.ProviderId, StringComparison.OrdinalIgnoreCase);
            }
            var matchingItem = Matches(media);
            var matchingShow = Matches(parent) ||
                (isEpisode && Parse(x.JellyfinItemId) == media.SeriesId && media.SeriesId != Guid.Empty) ||
                (isEpisode && !string.IsNullOrWhiteSpace(x.ProviderId) &&
                    media.SeriesProviderIds.TryGetValue(x.ProviderName ?? "Imdb", out var seriesProvider) &&
                    string.Equals(seriesProvider, x.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (scope == "movie" && media.Kind == "Movie" && matchingItem) return true;
            if (scope is "wholeshow" or "show" or "series" && isEpisode && matchingShow) return true;
            if (!isEpisode || (!matchingItem && !matchingShow)) continue;
            var season = media.Season ?? -1;
            var first = media.Episode ?? -1;
            var last = Math.Max(first, media.EpisodeEnd ?? first);
            var startSeason = x.StartSeason;
            var endSeason = Math.Max(startSeason, x.EndSeason);
            var from = x.StartEpisode;
            var to = Math.Max(from, x.EndEpisode);
            if (scope == "season" && season >= startSeason && season <= endSeason) return true;
            bool Inside(int number) => season >= startSeason && season <= endSeason &&
                (season > startSeason || number >= from) && (season < endSeason || number <= to);
            if (scope == "allowedrange" && (!Inside(first) || !Inside(last))) return true;
            if (scope == "episode" && ((matchingItem && from == 0) ||
                season >= startSeason && season <= endSeason &&
                (season > startSeason || last >= from) && (season < endSeason || first <= to))) return true;
        }
        return false;
    }

    private static string LogicalMediaKey(ChronologyMediaItem media, ChronologyMediaItem? parent)
    {
        static string? FirstProvider(IReadOnlyDictionary<string, string> providers) =>
            providers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => x.Key.ToLowerInvariant() + ":" + x.Value.Trim().ToLowerInvariant()).FirstOrDefault();
        if (media.Kind == "Episode")
        {
            var series = parent?.ProviderIds ?? media.SeriesProviderIds;
            var key = FirstProvider(series);
            if (key is not null && media.Season is >= 0 && media.Episode is >= 0)
                return "episode:" + key + ":" + media.Season.Value.ToString(CultureInfo.InvariantCulture) +
                    ":" + media.Episode.Value.ToString(CultureInfo.InvariantCulture) +
                    ":" + (media.EpisodeEnd ?? media.Episode).Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            var key = FirstProvider(media.ProviderIds);
            if (key is not null) return media.Kind.ToLowerInvariant() + ":" + key;
        }
        return "jellyfin:" + media.Id.ToString("N"); // provisional until Step 6 registry/recovery.
    }

    private static Guid Parse(string? value) => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    /// <summary>Deterministic RFC4122-shaped identifier; NOT yet the persisted Step 6 registry.</summary>
    private static Guid StableGuid(string text)
    {
        var data = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        data[6] = (byte)((data[6] & 0x0F) | 0x50);
        data[8] = (byte)((data[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(data.AsSpan(0, 16)).ToLowerInvariant();
        return Guid.ParseExact($"{hex[..8]}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20, 12)}", "D");
    }
}
