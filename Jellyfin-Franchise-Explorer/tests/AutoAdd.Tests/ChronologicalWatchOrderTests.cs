using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FranchiseExplorer.Configuration;
using Jellyfin.Plugin.FranchiseExplorer.Models;
using Jellyfin.Plugin.FranchiseExplorer.Services;

internal static class ChronologicalWatchOrderTests
{
    private sealed class FakeCatalog : IChronologyViewerCatalog
    {
        public Dictionary<Guid, ChronologyMediaItem> Items { get; } = new();
        public Dictionary<Guid, List<ChronologyMediaItem>> Collections { get; } = new();
        public Task<ChronologyMediaItem?> FindAsync(Guid id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Items.TryGetValue(id, out var media);
            return Task.FromResult(media);
        }
        public Task<IReadOnlyList<ChronologyMediaItem>> EpisodesAsync(Guid id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ChronologyMediaItem>>(
                Items.Values.Where(x => x.Kind == "Episode" && x.SeriesId == id).ToArray());
        }
        public Task<IReadOnlyList<ChronologyMediaItem>> CollectionMembersAsync(Guid id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ChronologyMediaItem>>(
                Collections.TryGetValue(id, out var media) ? media : []);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("Watch order: " + message);
    }

    private static string[] Names(ChronologyResolution result) => result.Items.Select(x => x.Name).ToArray();
    private static bool Is(ChronologyResolution result, params string[] expected) =>
        Names(result).SequenceEqual(expected);

    private static ChronologyMediaItem Movie(string name, string provider, bool playable = true) => new()
    {
        Id = Guid.NewGuid(), Name = name, Kind = "Movie", IsPlayable = playable,
        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Imdb"] = provider
        }
    };

    private static ChronologyMediaItem Episode(ChronologyMediaItem parent, int season, int ep, int? end = null) => new()
    {
        Id = Guid.NewGuid(), Name = "S" + season + "E" + ep, Kind = "Episode",
        SeriesId = parent.Id, Season = season, Episode = ep, EpisodeEnd = end,
        SeriesProviderIds = parent.ProviderIds
    };

    private static MediaLink Link(Guid nodeId, ChronologyMediaItem media, string rangeMode = "Full",
        int startSeason = 0, int endSeason = 0, int startEpisode = 0, int endEpisode = 0) => new()
    {
        Id = Guid.NewGuid().ToString("D"), NodeId = nodeId.ToString("D"),
        JellyfinItemId = media.Id.ToString("D"), ItemType = media.Kind,
        RangeMode = rangeMode, StartSeason = startSeason, EndSeason = endSeason,
        StartEpisode = startEpisode, EndEpisode = endEpisode
    };

    public static async Task Run()
    {
        var franchise = Guid.NewGuid();
        var user = Guid.NewGuid();
        var root = new FranchiseNode
        {
            Id = franchise.ToString("D"), Name = "Synthetic Franchise", NodeType = "Franchise"
        };
        var era = new FranchiseNode
        {
            Id = Guid.NewGuid().ToString("D"), ParentId = root.Id, Name = "Era",
            NodeType = "Era"
        };
        var otherFranchise = new FranchiseNode
        {
            Id = Guid.NewGuid().ToString("D"), NodeType = "Franchise", Name = "Other"
        };
        var show = new ChronologyMediaItem
        {
            Id = Guid.NewGuid(), Kind = "Series", Name = "Show",
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Imdb"] = "tt00123456"
            },
            IsPlayable = false
        };
        var ep1 = Episode(show, 1, 1);
        var ep2 = Episode(show, 1, 2);
        var ep3 = Episode(show, 1, 3);
        var special = Episode(show, 0, 7);
        var bundle = Episode(show, 2, 1, 2);
        var filmA = Movie("Film A", "tt-film-a");
        var filmB = Movie("Film B", "tt-film-b");
        var inaccessible = Movie("Hidden film", "tt-private");
        var unavailable = Movie("Not playable", "tt-unavailable", playable: false);
        var boxset = new ChronologyMediaItem { Id = Guid.NewGuid(), Kind = "BoxSet", Name = "Boxset" };
        var cat = new FakeCatalog();
        foreach (var item in new[] { show, ep1, ep2, ep3, special, bundle, filmA, filmB, unavailable, boxset })
            cat.Items[item.Id] = item;
        // The fake never returns the inaccessible film, just as the permission-scoped adapter must not.
        cat.Collections[boxset.Id] = [filmB, show];

        var filmRef = Link(franchise, filmA);
        var part1 = Link(franchise, show, "Episodes", 1, 1, 1, 2);
        var filmBRef = Link(franchise, filmB);
        var part2 = Link(franchise, show, "Episodes", 1, 1, 3, 3);
        var row = new FranchiseRow
        {
            Id = Guid.NewGuid().ToString("D"), ParentNodeId = era.Id, Name = "Chronology",
            RowType = "Timeline",
            MediaLinkIds = [part1.Id, filmBRef.Id, filmRef.Id, part2.Id],
            // Deliberately differs from the source Link list order.
            MediaOrder = [filmRef.Id, part1.Id, filmBRef.Id, part2.Id]
        };
        var config = new PluginConfiguration
        {
            Nodes = [root, era, otherFranchise],
            Rows = [row],
            MediaLinks = [filmRef, part1, filmBRef, part2],
            ConfigurationRevision = 9
        };
        var resolver = new ChronologicalWatchOrderResolver();
        var resolved = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(Is(resolved, "Film A", "S1E1", "S1E2", "Film B", "S1E3"),
            "configured interleaving overrides link order, generic show order and catalog order");
        Check(resolved.Items.All(x => x.OccurrenceId != Guid.Empty), "all occurrences must have IDs");
        Check(resolved.Items.Select(x => x.OccurrenceId).Distinct().Count() == 5, "all occurrences distinct");
        Check(resolved.Items.Select(x => x.Position).SequenceEqual(Enumerable.Range(0, 5)),
            "zero-based sequential output positions");
        Check(resolved.PreviewOnly && !resolved.IsCanonicalTracking, "Step 5 must never create tracked progress");
        Check(resolved.OrderId.ToString("D") == row.Id && resolved.FranchiseId == franchise,
            "same canonical row and franchise ID");
        Check(resolved.OrderRevision == 9, "config revision is surfaced");
        var repeated = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(resolved.QueueSnapshotId == repeated.QueueSnapshotId, "same authorized view must yield stable preview digest");
        Check(resolved.Items.Select(x => x.OccurrenceId).SequenceEqual(repeated.Items.Select(x => x.OccurrenceId)),
            "same physical mapping must yield stable identities");

        // Reorder only: media occurrences retain identity even after positions change.
        row.MediaOrder = [filmRef.Id, filmBRef.Id, part1.Id, part2.Id];
        config.ConfigurationRevision++;
        var reordered = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(Is(reordered, "Film A", "Film B", "S1E1", "S1E2", "S1E3"),
            "new explicit order wins");
        Check(resolved.QueueSnapshotId != reordered.QueueSnapshotId, "a modified order creates a distinct preview");
        foreach (var item in resolved.Items)
            Check(reordered.Items.Single(x => x.Name == item.Name).OccurrenceId == item.OccurrenceId,
                "reordering must preserve occurrence " + item.Name);

        // Same movie from two DISTINCT references creates two independent occurrences.
        var secondFilmA = Link(franchise, filmA);
        row.MediaLinkIds.Add(secondFilmA.Id);
        row.MediaOrder.Add(secondFilmA.Id);
        config.MediaLinks.Add(secondFilmA);
        var duplicate = await resolver.ResolveAsync(config, franchise, user, cat);
        var copies = duplicate.Items.Where(x => x.JellyfinItemId == filmA.Id).ToArray();
        Check(copies.Length == 2 && copies[0].OccurrenceId != copies[1].OccurrenceId,
            "same Jellyfin movie in separate slots must have different occurrence identities");
        Check(copies[0].SlotId != copies[1].SlotId, "distinct slot identity required");
        row.MediaLinkIds.Remove(secondFilmA.Id);
        row.MediaOrder.Remove(secondFilmA.Id);
        config.MediaLinks.Remove(secondFilmA);

        // No matching user visibility -> warning, never a hidden-title leak.
        var hidden = Link(franchise, inaccessible);
        row.MediaLinkIds.Add(hidden.Id);
        row.MediaOrder.Add(hidden.Id);
        config.MediaLinks.Add(hidden);
        var withHidden = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(!withHidden.Items.Any(x => x.JellyfinItemId == inaccessible.Id),
            "inaccessible media must never enter a user's queue");
        Check(withHidden.Warnings.Any(x => x.Code == "missing_or_forbidden"), "hidden/missing warning required");
        Check(!withHidden.Warnings.Any(x => x.Message.Contains(inaccessible.Name, StringComparison.Ordinal)),
            "hidden item's name cannot appear in warnings");
        row.MediaLinkIds.Remove(hidden.Id); row.MediaOrder.Remove(hidden.Id); config.MediaLinks.Remove(hidden);

        // Complete-series Full ignores specials; explicit direct special is retained.
        var full = Link(franchise, show, "Full");
        row.MediaLinkIds = [full.Id];
        row.MediaOrder = [full.Id];
        config.MediaLinks = [full];
        var fullResult = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(Is(fullResult, "S1E1", "S1E2", "S1E3", "S2E1"),
            "Full must not automatically insert specials and must include multipart file once");
        row.Items = [new FranchiseRowEntry
        {
            Id = Guid.NewGuid().ToString("D"), JellyfinItemId = special.Id.ToString("D"),
            ItemType = "Episode", RangeMode = "Full"
        }];
        row.MediaOrder.Add(row.Items[0].Id);
        var explicitSpecial = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(explicitSpecial.Items.Last().JellyfinItemId == special.Id,
            "explicitly placed special must be played despite Full default excluding season 0");

        // Multipart file intersects requested episode 2, but is one physical file.
        var multi = Link(franchise, show, "Episodes", 2, 2, 2, 2);
        row.MediaLinkIds = [multi.Id]; row.Items = []; row.MediaOrder = [multi.Id];
        config.MediaLinks = [multi];
        var multipart = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(Is(multipart, "S2E1") && multipart.Items[0].EpisodeEnd == 2,
            "multipart file must not be split into imaginary episode units");

        // BoxSet supports nested series, preserving native collection member order.
        var collection = Link(franchise, boxset);
        row.MediaLinkIds = [collection.Id]; row.MediaOrder = [collection.Id];
        config.MediaLinks = [collection];
        var collectionResult = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(Is(collectionResult, "Film B", "S1E1", "S1E2", "S1E3", "S2E1"),
            "boxset member order and nested series expansion");

        // Exclusions scoped to this exact row omit the intended episode.
        config.AutoFranchises.Exclusions.Add(new AutoFranchiseExclusion
        {
            FranchiseNodeId = franchise.ToString("D"), RowId = row.Id, Scope = "Episode",
            ProviderId = "tt00123456", ProviderName = "Imdb",
            StartSeason = 1, EndSeason = 1, StartEpisode = 2, EndEpisode = 2
        });
        var exclude = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(!exclude.Items.Any(x => x.Name == "S1E2"), "episode exclusion must apply");
        Check(exclude.Items.Any(x => x.Name == "S1E1") && exclude.Items.Any(x => x.Name == "S1E3"),
            "exclusions must not eliminate the entire show");

        // Invalid / missing media and overly many rows fail visibly, not guessed.
        config.AutoFranchises.Exclusions.Clear();
        row.MediaLinkIds = [Guid.NewGuid().ToString("D")];
        row.MediaOrder = [];
        var missing = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(missing.Items.Count == 0 &&
            missing.Warnings.Any(x => x.Code == "missing_reference"), "orphan reference not silently replaced");

        var extra = new FranchiseRow { Id = Guid.NewGuid().ToString("D"), ParentNodeId = root.Id, RowType = "Timeline" };
        config.Rows.Add(extra);
        try { await resolver.ResolveAsync(config, franchise, user, cat); throw new Exception("should reject ambiguity"); }
        catch (ChronologyResolutionException ex) { Check(ex.Code == "ambiguous_primary_order", "ambiguous chronology must fail closed"); }
        root.PrimaryChronologyRowId = row.Id;
        var explicitPrimary = await resolver.ResolveAsync(config, franchise, user, cat);
        Check(explicitPrimary.OrderId.ToString("D") == row.Id, "explicit primary selector must be honored");
        root.PrimaryChronologyRowId = extra.Id;
        Check((await resolver.ResolveAsync(config, franchise, user, cat)).OrderId.ToString("D") == extra.Id,
            "admin may select a different valid chronology");
        root.PrimaryChronologyRowId = Guid.NewGuid().ToString("D");
        try { await resolver.ResolveAsync(config, franchise, user, cat); throw new Exception("should reject invalid primary"); }
        catch (ChronologyResolutionException ex) { Check(ex.Code == "invalid_primary_order", "invalid primary must fail closed"); }
        root.PrimaryChronologyRowId = row.Id;

        var future = new FranchiseRow
        {
            Id = Guid.NewGuid().ToString("D"), ParentNodeId = otherFranchise.Id, RowType = "Timeline"
        };
        config.Rows.Remove(extra); config.Rows.Add(future);
        Check((await resolver.ResolveAsync(config, franchise, user, cat)).OrderId.ToString("D") == row.Id,
            "timeline in unrelated franchise must not affect canonical selection");

        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await resolver.ResolveAsync(config, franchise, user, cat, cancellation.Token); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { }

        Console.WriteLine("PASS Step 5 canonical server resolver: chronology, episode ranges, multipart/specials, nested collections, duplicate occurrences, scoped exclusions, access failures, stable IDs and primary selection");
    }
}
