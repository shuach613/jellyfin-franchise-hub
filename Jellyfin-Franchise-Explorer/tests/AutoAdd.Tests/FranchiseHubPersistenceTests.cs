using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.FranchiseExplorer.Api;
using Jellyfin.Plugin.FranchiseExplorer.Configuration;
using Jellyfin.Plugin.FranchiseExplorer.Models;
using Jellyfin.Plugin.FranchiseExplorer.Services;
using Microsoft.Data.Sqlite;

internal static class FranchiseHubPersistenceTests
{
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("Persistence: " + message);
    }
    private static ChronologyOccurrence Item(Guid reference, Guid item, string logical, int index) => new()
    {
        OccurrenceId = Guid.NewGuid(), SlotId = reference, ReferenceId = reference,
        JellyfinItemId = item, SourceItemId = item, LogicalMediaKey = logical,
        Kind = "Movie", Name = "Synthetic " + index, Position = index
    };
    private static ChronologyResolution Preview(Guid franchise, Guid order, Guid snapshot,
        long revision, params ChronologyOccurrence[] items) => new()
    {
        FranchiseId = franchise, OrderId = order, QueueSnapshotId = snapshot,
        OrderRevision = revision, Items = items
    };
    private static string Progress(Guid row, string[] queue, string[] completed, int index, long ticks)
        => JsonSerializer.Serialize(new FranchiseTimelineController.TimelineProgress
        {
            RowId = row.ToString("D"), QueueKeys = queue.ToList(), CompletedKeys = completed.ToList(),
            Index = index, Total = queue.Length, PositionTicks = ticks, Name = "Synthetic",
            PlaybackMode = "paused"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static Task Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "franchise-hub-step6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new FranchiseHubStateStore(folder);
            Check(store.DatabaseSchemaVersion == 1, "schema migration initialized to version 1");
            Check(store.IntegrityCheck(), "SQLite integrity check passed");

            var user = Guid.NewGuid();
            var otherUser = Guid.NewGuid();
            var franchise = Guid.NewGuid();
            var order = Guid.NewGuid();
            var refA = Guid.NewGuid();
            var refB = Guid.NewGuid();
            var mediaA = Guid.NewGuid();
            var mediaB = Guid.NewGuid();
            var input = Preview(franchise, order, Guid.NewGuid(), 11,
                Item(refA, mediaA, "movie:imdb:tta", 0),
                Item(refB, mediaB, "movie:imdb:ttb", 1));
            var first = store.RegisterSnapshot(input, user);
            Check(!first.IsCanonicalTracking && first.PreviewOnly, "snapshot must remain preview-only");
            Check(first.Items.Count == 2, "two source items");
            Check(first.Items[0].OccurrenceId != input.Items[0].OccurrenceId, "provisional IDs replaced with persisted IDs");
            Check(first.Items[0].OccurrenceId != first.Items[1].OccurrenceId, "independent occurrences");
            Check(store.ReadSnapshot(user, first.QueueSnapshotId)?.Items.Count == 2,
                "right user reads immutable snapshot");
            Check(store.ReadSnapshot(otherUser, first.QueueSnapshotId) is null,
                "wrong user cannot read persisted preview");
            var same = store.RegisterSnapshot(input, user);
            Check(same.Items[0].OccurrenceId == first.Items[0].OccurrenceId &&
                  same.Items[1].OccurrenceId == first.Items[1].OccurrenceId,
                "repeat resolve must preserve occurrence IDs and snapshot digest");
            Check(store.ReadProgress(user, franchise, order) is null,
                "resolving previews must never create watch progress");

            var changed = Preview(franchise, order, Guid.NewGuid(), 12,
                Item(refB, mediaB, "movie:imdb:ttb", 0),
                Item(refA, Guid.NewGuid(), "movie:imdb:tta", 1)); // provider-verified physical remap
            var resolved = store.RegisterSnapshot(changed, user);
            Check(resolved.Items[0].OccurrenceId == first.Items[1].OccurrenceId &&
                  resolved.Items[1].OccurrenceId == first.Items[0].OccurrenceId,
                "reorder/rescan retains durable occurrence identity");
            Check(store.IntegrityCheck(), "post-update database integrity");

            // Distinct slots of a repeated physical file remain different.
            var repeatedRef = Guid.NewGuid();
            var repeated = store.RegisterSnapshot(Preview(franchise, order, Guid.NewGuid(), 13,
                Item(refA, mediaA, "movie:imdb:tta", 0),
                Item(repeatedRef, mediaA, "movie:imdb:tta", 1)), user);
            Check(repeated.Items[0].OccurrenceId != repeated.Items[1].OccurrenceId,
                "same media duplicated on two placements must be distinguishable");

            // A new instance against the same DB should read the same registry.
            var restart = new FranchiseHubStateStore(folder);
            var reread = restart.RegisterSnapshot(Preview(franchise, order, Guid.NewGuid(), 14,
                Item(refA, mediaA, "movie:imdb:tta", 0),
                Item(refB, mediaB, "movie:imdb:ttb", 1)), user);
            Check(reread.Items[0].OccurrenceId == first.Items[0].OccurrenceId,
                "persistent identity survives service restart");
            Check(restart.DatabaseSchemaVersion == 1, "schema migration idempotent");

            // Build the exact legacy /timeline-progress/<user:N>/<row:N>.json convention.
            var staging = Path.Combine(folder, "legacy-staging", "timeline-progress", user.ToString("N"));
            Directory.CreateDirectory(staging);
            var legacyPath = Path.Combine(staging, order.ToString("N") + ".json");
            var keyA = refA.ToString("D") + ":" + mediaA.ToString("D");
            var keyB = refB.ToString("D") + ":" + mediaB.ToString("D");
            var oldJson = Progress(order, [keyA, keyB], [keyA], 1, 220000000);
            File.WriteAllText(legacyPath, oldJson);
            var art = Path.Combine(folder, "franchise-artwork", "assets");
            Directory.CreateDirectory(art);
            File.WriteAllBytes(Path.Combine(art, "synthetic.png"), [1, 2, 3, 4]);
            var node = new FranchiseNode
            {
                Id = franchise.ToString("D"), NodeType = "Franchise",
                PrimaryChronologyRowId = order.ToString("D")
            };
            var row = new FranchiseRow
            {
                Id = order.ToString("D"), ParentNodeId = node.Id, RowType = "Timeline"
            };
            var config = new PluginConfiguration { Nodes = [node], Rows = [row], ConfigurationRevision = 14 };
            var migration = new LegacyTimelineMigrationService(folder, store);
            var preview = migration.Preview(config);
            Check(preview.Records == 1 && preview.Ready == 1 && preview.PendingReview == 0 &&
                  !preview.AlreadyImported, "unique verified old IDs importable");
            var backupXml = Encoding.UTF8.GetBytes("<PluginConfiguration><NavigationLabel>Franchises</NavigationLabel></PluginConfiguration>");
            var result = migration.Apply(config, preview.SourceDigest, backupXml);
            Check(!result.AlreadyImported && result.ImportedRecords == 1 &&
                  result.PendingReview == 0, "atomic one-row legacy import");
            var imported = store.ReadProgress(user, franchise, order);
            Check(imported is not null && imported.CursorOccurrenceId == first.Items[1].OccurrenceId,
                "correct migrated cursor occurrence");
            Check(imported!.PositionTicks == 220000000 && imported.CompletedOccurrences.Count == 1 &&
                  imported.CompletedOccurrences[0] == first.Items[0].OccurrenceId,
                "resume ticks and completed history survive migration");
            Check(File.ReadAllText(legacyPath) == oldJson, "original staged JSON is never changed");
            var zip = Path.Combine(folder, "franchise-hub-state", "migration-backups", result.BackupName);
            Check(File.Exists(zip) && new FileInfo(zip).Length > 0, "backup created before migrated SQL");
            using (var backup = ZipFile.OpenRead(zip))
            {
                Check(backup.Entries.Any(x => x.FullName.EndsWith("/" + order.ToString("N") + ".json", StringComparison.Ordinal)),
                    "backup includes original JSON");
                Check(backup.GetEntry("existing-franchise-hub-configuration.xml") is not null,
                    "backup includes current configuration");
                Check(backup.GetEntry("franchise-artwork/assets/synthetic.png") is not null,
                    "backup includes artwork");
                Check(backup.GetEntry("backup-manifest.json") is not null,
                    "backup includes checksums manifest");
            }
            var retry = migration.Apply(config, preview.SourceDigest, backupXml);
            Check(retry.AlreadyImported, "repeated migration is idempotent");

            // New separate user record with the same row but NO per-user
            // immutable resolved queue => retain it pending_review instead
            // of falsely claiming it was watched.
            var newUser = Guid.NewGuid();
            var otherFolder = Path.Combine(folder, "legacy-staging", "timeline-progress", newUser.ToString("N"));
            Directory.CreateDirectory(otherFolder);
            var otherJson = Progress(order, [keyA, keyB], [keyA], 1, 100000000);
            var otherPath = Path.Combine(otherFolder, order.ToString("N") + ".json");
            File.WriteAllText(otherPath, otherJson);
            var secondPreview = migration.Preview(config);
            Check(secondPreview.Records == 2 && secondPreview.PendingReview >= 1,
                "missing viewer-specific snapshot requires review");
            var secondImport = migration.Apply(config, secondPreview.SourceDigest, backupXml);
            Check(secondImport.PendingReview >= 1 &&
                store.ReadProgress(newUser, franchise, order) is null,
                "migration never invents progress for missing snapshot");
            Check(store.ReadProgress(user, franchise, order)?.CursorOccurrenceId == first.Items[1].OccurrenceId,
                "earlier progress never overwritten by later migration");

            // Tamper after preview => fail without modifying any current watch state.
            var stagedPreview = migration.Preview(config);
            File.AppendAllText(otherPath, " ");
            try
            {
                migration.Apply(config, stagedPreview.SourceDigest, backupXml);
                throw new Exception("source mutation should be rejected");
            }
            catch (InvalidOperationException) { }
            Check(store.IntegrityCheck(), "transactional integrity after rejected migration");

            // Forward-only schema: a future-version database must refuse downgrades.
            var newer = Path.Combine(Path.GetTempPath(), "franchise-hub-future-" + Guid.NewGuid().ToString("N"));
            try
            {
                var futureStore = new FranchiseHubStateStore(newer);
                Check(futureStore.DatabaseSchemaVersion == 1, "isolated future schema baseline");
                var dbPath = Path.Combine(newer, "franchise-hub-state", "state.sqlite3");
                using (var db = new SqliteConnection("Data Source=" + dbPath))
                {
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "PRAGMA user_version=99;";
                    cmd.ExecuteNonQuery();
                }
                try
                {
                    _ = new FranchiseHubStateStore(newer).DatabaseSchemaVersion;
                    throw new Exception("future schema version was accepted");
                }
                catch (InvalidOperationException) { }
            }
            finally { if (Directory.Exists(newer)) Directory.Delete(newer, recursive: true); }

            Console.WriteLine("PASS Step 6 SQLite: schema, unique occurrence registry, viewer isolation, immutable snapshots, reboot, backup-first legacy import, idempotency, pending review, integrity and downgrade protection");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        return Task.CompletedTask;
    }
}
