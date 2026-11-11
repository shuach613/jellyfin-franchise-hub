using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.FranchiseExplorer.Api;
using Jellyfin.Plugin.FranchiseExplorer.Configuration;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.FranchiseExplorer.Services;

/// <summary>
/// Explicit, backup-first import of COPIED legacy JSON progress. The original
/// Explorer's data directory is never guessed, scanned or mutated. Operators
/// place copies under Franchise Hub's legacy-staging/timeline-progress folder.
/// Original configuration transfer belongs to the Step 7 adapter.
/// </summary>
public sealed class LegacyTimelineMigrationService
{
    private const int MaxRecords = 1000;
    private const long MaxRecordBytes = 128 * 1024;
    private const long MaxBackupBytes = 512L * 1024 * 1024;
    private readonly string _root;
    private readonly FranchiseHubStateStore _store;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly object Gate = new();

    public LegacyTimelineMigrationService(string pluginDataRoot, FranchiseHubStateStore store)
    {
        _root = Path.GetFullPath(pluginDataRoot);
        _store = store;
    }

    public sealed record LegacyMigrationPreview(
        string SourceDigest, int Records, int Ready, int PendingReview,
        IReadOnlyList<string> Warnings, bool AlreadyImported);

    public sealed record LegacyMigrationResult(
        bool AlreadyImported, int ImportedRecords, int PendingReview,
        string BackupName, string SourceDigest);

    public sealed record LegacyRollbackResult(bool AlreadyRolledBack,
        int DeletedJourneyRows, string BackupName, string SourceDigest);


    private sealed record LegacyFile(Guid UserId, Guid RowId, string Relative,
        string JsonText, byte[] Bytes, string Digest,
        FranchiseTimelineController.TimelineProgress Progress);

    private sealed class CheckedSource
    {
        public required string Digest { get; init; }
        public required List<LegacyFile> Files { get; init; }
    }

    private static string Sha(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
    private static string Id(Guid value) => value.ToString("D");
    private static bool SafeFile(string path)
    {
        if (!File.Exists(path)) return false;
        var a = File.GetAttributes(path);
        return (a & FileAttributes.ReparsePoint) == 0;
    }

    private CheckedSource ReadStaged()
    {
        var stagingRoot = Path.Combine(_root, "legacy-staging");
        if (Directory.Exists(stagingRoot) &&
            (File.GetAttributes(stagingRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Staging root cannot be a symlink.");
        var folder = Path.Combine(stagingRoot, "timeline-progress");
        if (!Directory.Exists(folder))
            return new CheckedSource { Files = [], Digest = Sha([]) };
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Staging folder cannot be a symlink.");
        var found = new List<LegacyFile>();
        long total = 0;
        // Only exactly <user-guid-N>/<row-guid-N>.json; no recursive paths,
        // special files, symlink traversal or caller-supplied absolute paths.
        foreach (var directory in Directory.EnumerateDirectories(folder).OrderBy(x => x, StringComparer.Ordinal))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A user folder is a symlink.");
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var user) || user == Guid.Empty)
                throw new InvalidDataException("Invalid user directory in migration staging.");
            foreach (var path in Directory.EnumerateFiles(directory).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!SafeFile(path)) throw new InvalidDataException("A staged file is missing or a symlink.");
                if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var row) || row == Guid.Empty)
                    throw new InvalidDataException("Staging contains an unexpected file.");
                if (found.Count >= MaxRecords) throw new InvalidDataException("Too many legacy progress records.");
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxRecordBytes) throw new InvalidDataException("Invalid legacy record size.");
                total = checked(total + info.Length);
                if (total > MaxBackupBytes) throw new InvalidDataException("Legacy progress exceeds backup limit.");
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length != info.Length) throw new IOException("A staged record changed while reading.");
                var text = Encoding.UTF8.GetString(bytes);
                var state = JsonSerializer.Deserialize<FranchiseTimelineController.TimelineProgress>(text, Json)
                    ?? throw new InvalidDataException("Legacy record is empty.");
                if (!Guid.TryParse(state.RowId, out var embedded) || embedded != row ||
                    state.Index < 0 || state.QueueKeys is null || state.CompletedKeys is null ||
                    state.QueueKeys.Count > 25000 || state.CompletedKeys.Count > 25000 ||
                    state.Index > state.QueueKeys.Count || state.PositionTicks < 0 ||
                    state.QueueKeys.Any(k => k.Length > 140) ||
                    state.CompletedKeys.Any(k => k.Length > 140))
                    throw new InvalidDataException("Legacy row IDs or queue bounds are invalid.");
                var relative = user.ToString("N") + "/" + row.ToString("N") + ".json";
                found.Add(new LegacyFile(user, row, relative, text, bytes, Sha(bytes), state));
            }
        }
        var hashInput = string.Join("\n", found.Select(x => x.Relative + " " + x.Digest));
        return new CheckedSource { Files = found, Digest = Sha(Encoding.UTF8.GetBytes(hashInput)) };
    }

    private sealed record Mapping(Guid FranchiseId, Guid OrderId, long Revision,
        Guid? Cursor, IReadOnlyList<Guid> Completed, string? Reason);

    private static (Guid Reference, Guid Item)? DecodeKey(string key)
    {
        var split = key.LastIndexOf(':');
        if (split <= 0 || split >= key.Length - 1) return null;
        if (!Guid.TryParse(key[..split], out var reference) ||
            !Guid.TryParse(key[(split + 1)..], out var item)) return null;
        return (reference, item);
    }

    private static Guid FindRoot(PluginConfiguration cfg, Guid parent)
    {
        var seen = new HashSet<Guid>();
        while (parent != Guid.Empty && seen.Add(parent))
        {
            var node = cfg.Nodes.FirstOrDefault(x => Guid.TryParse(x.Id, out var g) && g == parent);
            if (node is null) return Guid.Empty;
            if (string.Equals(node.NodeType, "Franchise", StringComparison.OrdinalIgnoreCase))
                return parent;
            parent = Guid.TryParse(node.ParentId, out var p) ? p : Guid.Empty;
        }
        return Guid.Empty;
    }

    private static bool PrimaryOrder(PluginConfiguration cfg, Guid root, Guid order)
    {
        var node = cfg.Nodes.FirstOrDefault(x => Guid.TryParse(x.Id, out var id) && id == root);
        if (node is null) return false;
        if (Guid.TryParse(node.PrimaryChronologyRowId, out var configured))
            return configured == order;
        var candidates = cfg.Rows.Where(r => r.RowType == "Timeline" &&
            Guid.TryParse(r.ParentNodeId, out var parent) && FindRoot(cfg, parent) == root).ToArray();
        return candidates.Length == 1 &&
            Guid.TryParse(candidates[0].Id, out var valid) && valid == order;
    }

    private Mapping Map(SqliteConnection db, SqliteTransaction tx, PluginConfiguration config, LegacyFile file)
    {
        var row = config.Rows.FirstOrDefault(r =>
            Guid.TryParse(r.Id, out var id) && id == file.RowId && r.RowType == "Timeline");
        if (row is null)
            return new(Guid.Empty, file.RowId, 0, null, [], "Legacy row is not a configured Timeline row.");
        var root = FindRoot(config, Guid.TryParse(row.ParentNodeId, out var parent) ? parent : Guid.Empty);
        if (root == Guid.Empty || !PrimaryOrder(config, root, file.RowId))
            return new(root, file.RowId, 0, null, [], "The legacy row is not the unambiguous canonical chronology.");
        var snapshotRevision = FranchiseHubStateStore.SqlValue(db, tx,
            @"SELECT MAX(revision) FROM sequence_snapshots WHERE order_id=$o AND viewer_user_id=$u",
            ("$o", Id(file.RowId)), ("$u", Id(file.UserId)));
        if (!long.TryParse(snapshotRevision, out var revision) ||
            revision != config.ConfigurationRevision)
            return new(root, file.RowId, 0, null, [],
                "No viewer-bound queue snapshot exists for the current configuration revision.");
        var order = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        // Legacy references alone can be ambiguous. Only an exact (slotId,
        // physical itemId) pair with one occurrence in this user's current
        // immutable snapshot is convertible; never guess by title.
        var snapshotJson = FranchiseHubStateStore.SqlValue(db, tx,
            @"SELECT json FROM sequence_snapshots WHERE order_id=$o AND viewer_user_id=$u
             AND revision=$r ORDER BY created_utc DESC LIMIT 1",
            ("$o", Id(file.RowId)), ("$u", Id(file.UserId)), ("$r", revision));
        var snapshot = snapshotJson is null ? null : JsonSerializer.Deserialize<Jellyfin.Plugin.FranchiseExplorer.Models.ChronologyResolution>(snapshotJson, Json);
        if (snapshot is null)
            return new(root, file.RowId, 0, null, [], "The persisted sequence snapshot is unavailable.");
        foreach (var group in snapshot.Items.GroupBy(x => Id(x.ReferenceId) + ":" + Id(x.JellyfinItemId),
            StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() == 1) order[group.Key] = group.Single().OccurrenceId;
        }
        Guid? Translate(string key)
        {
            var parts = DecodeKey(key);
            if (parts is null) return null;
            return order.TryGetValue(Id(parts.Value.Reference) + ":" + Id(parts.Value.Item), out var occurrence)
                ? occurrence : null;
        }
        // Preserve all history exactly; conversion requires every relevant
        // queue/completed key to map uniquely to a CURRENT occurrence.
        if (file.Progress.QueueKeys.Any(key => Translate(key) is null) ||
            file.Progress.CompletedKeys.Any(key => Translate(key) is null))
            return new(root, file.RowId, revision, null, [],
                "One or more references are missing, remapped or ambiguous; review the preserved legacy record.");

        var completions = file.Progress.CompletedKeys.Select(key => Translate(key)!.Value).Distinct().ToArray();
        var cursor = file.Progress.Index == file.Progress.QueueKeys.Count
            ? (Guid?)null : Translate(file.Progress.QueueKeys[file.Progress.Index]);
        if (cursor is null && file.Progress.Index < file.Progress.QueueKeys.Count)
            return new(root, file.RowId, revision, null, [], "Legacy cursor is ambiguous.");
        var existing = FranchiseHubStateStore.SqlValue(db, tx,
            @"SELECT state_version FROM journey_progress WHERE user_id=$u AND franchise_id=$f AND order_id=$o",
            ("$u", Id(file.UserId)), ("$f", Id(root)), ("$o", Id(file.RowId)));
        if (existing is not null)
            return new(root, file.RowId, revision, null, [], "New-format progress already exists; never overwrite it.");
        return new(root, file.RowId, revision, cursor, completions, null);
    }

    public LegacyMigrationPreview Preview(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var source = ReadStaged();
        return _store.InTransaction((db, tx) =>
        {
            var imported = FranchiseHubStateStore.SqlValue(db, tx,
                "SELECT import_id FROM legacy_import_journal WHERE source_digest=$digest",
                ("$digest", source.Digest));
            var ready = 0; var pending = 0;
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in source.Files)
            {
                var mapping = Map(db, tx, configuration, file);
                if (mapping.Reason is null) ready++;
                else { pending++; reasons.Add(mapping.Reason); }
            }
            return new LegacyMigrationPreview(source.Digest, source.Files.Count,
                ready, pending, reasons.ToArray(), imported is not null);
        });
    }

    private static void AddZip(ZipArchive zip, string name, byte[] data)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = DateTimeOffset.UtcNow;
        using var dst = entry.Open();
        dst.Write(data);
    }

    private string CreateBackup(CheckedSource source, byte[]? existingConfiguration)
    {
        var destination = Path.Combine(_root, "franchise-hub-state", "migration-backups");
        Directory.CreateDirectory(destination);
        var name = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N") + ".zip";
        var output = Path.Combine(destination, name);
        var temporary = output + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var file in source.Files)
                {
                    var entry = "legacy/timeline-progress/" + file.Relative;
                    AddZip(zip, entry, file.Bytes);
                    hashes[entry] = file.Digest;
                }
                if (existingConfiguration is { Length: > 0 })
                {
                    if (existingConfiguration.Length > 16 * 1024 * 1024)
                        throw new InvalidDataException("Plugin configuration exceeds backup size limit.");
                    AddZip(zip, "existing-franchise-hub-configuration.xml", existingConfiguration);
                    hashes["existing-franchise-hub-configuration.xml"] = Sha(existingConfiguration);
                }
                // Preserve both currently installed Franchise Hub artwork and
                // copies of Explorer artwork staged for eventual Step 7 import.
                // Walk one directory at a time: symlinks are rejected, never
                // followed while recursively enumerating untrusted staging.
                long total = source.Files.Sum(x => (long)x.Bytes.Length);
                void IncludeArtwork(string root, string prefix)
                {
                    if (!Directory.Exists(root)) return;
                    void Walk(string current)
                    {
                        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                            throw new InvalidDataException("Artwork staging contains a symlink.");
                        foreach (var path in Directory.EnumerateFileSystemEntries(current))
                        {
                            var attrs = File.GetAttributes(path);
                            if ((attrs & FileAttributes.ReparsePoint) != 0)
                                throw new InvalidDataException("Artwork staging contains a symlink.");
                            if ((attrs & FileAttributes.Directory) != 0) { Walk(path); continue; }
                            var length = new FileInfo(path).Length;
                            total = checked(total + length);
                            if (total > MaxBackupBytes) throw new InvalidDataException("Backup exceeds allowed size.");
                            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                            if (relative.Split('/').Any(part => part is "." or ".."))
                                throw new InvalidDataException("Unsafe artwork path.");
                            var bytes = File.ReadAllBytes(path);
                            if (bytes.LongLength != length) throw new IOException("Artwork changed during backup.");
                            var entry = prefix + "/" + relative;
                            AddZip(zip, entry, bytes);
                            hashes[entry] = Sha(bytes);
                        }
                    }
                    Walk(root);
                }
                IncludeArtwork(Path.Combine(_root, "franchise-artwork"), "franchise-artwork");
                IncludeArtwork(Path.Combine(_root, "legacy-staging", "franchise-artwork"),
                    "legacy-explorer-artwork");

                var stagedXmlPath = Path.Combine(_root, "legacy-staging",
                    "original-plugin-configuration.xml");
                if (File.Exists(stagedXmlPath))
                {
                    if (!SafeFile(stagedXmlPath))
                        throw new InvalidDataException("Original configuration cannot be a symlink.");
                    var oldXml = File.ReadAllBytes(stagedXmlPath);
                    if (oldXml.Length is 0 or > 16 * 1024 * 1024)
                        throw new InvalidDataException("Original configuration exceeds allowed size.");
                    AddZip(zip, "original-explorer-configuration.xml", oldXml);
                    hashes["original-explorer-configuration.xml"] = Sha(oldXml);
                }
                var manifest = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1, sourceDigest = source.Digest,
                    savedAtUtc = DateTimeOffset.UtcNow, files = hashes
                }, Json);
                AddZip(zip, "backup-manifest.json", Encoding.UTF8.GetBytes(manifest));
            }
            // Backup must exist durably before any SQLite user-state transaction.
            File.Move(temporary, output);
            return name;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }


    /// <summary>
    /// Reopen the completed private backup and verify that every archive entry
    /// is exactly covered by its SHA-256 manifest before SQL import begins.
    /// Never extracts archive paths to disk.
    /// </summary>
    private static void VerifyBackup(string archivePath, string sourceDigest)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var manifestEntry = zip.GetEntry("backup-manifest.json")
            ?? throw new InvalidDataException("Backup manifest is missing.");
        if (manifestEntry.Length <= 0 || manifestEntry.Length > 1024 * 1024)
            throw new InvalidDataException("Backup manifest has invalid size.");
        using var manifestStream = manifestEntry.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        var root = manifest.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("sourceDigest").GetString() != sourceDigest)
            throw new InvalidDataException("Backup manifest is for a different migration.");
        var files = root.GetProperty("files");
        if (files.ValueKind != JsonValueKind.Object || files.EnumerateObject().Count() > 100000)
            throw new InvalidDataException("Backup manifest contains invalid file records.");
        var expected = files.EnumerateObject().ToDictionary(
            x => x.Name, x => x.Value.GetString() ?? "", StringComparer.Ordinal);
        if (expected.Count + 1 != zip.Entries.Count)
            throw new InvalidDataException("Backup contains unlisted or duplicate entries.");
        long total = 0;
        foreach (var (name, hash) in expected)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("/", StringComparison.Ordinal) ||
                name.Split('/').Any(p => p is "" or "." or "..") || name.Contains('\\'))
                throw new InvalidDataException("Backup contains an unsafe entry name.");
            var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Backup archive is incomplete.");
            if (entry.Length < 0) throw new InvalidDataException("Invalid backup entry length.");
            total = checked(total + entry.Length);
            if (total > MaxBackupBytes + 32L * 1024 * 1024)
                throw new InvalidDataException("Backup is larger than the approved limit.");
            using var stream = entry.Open();
            var checksum = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(checksum, hash, StringComparison.Ordinal))
                throw new InvalidDataException("Backup checksum verification failed.");
        }
    }

    /// <summary>
    /// Roll back a migration only while every migrated journey remains at
    /// state_version=0 and has no active lease. Snapshots and backup ZIPs
    /// remain untouched; old JSON/config/art are never overwritten.
    /// </summary>
    public LegacyRollbackResult Rollback(string sourceDigest)
    {
        if (sourceDigest.Length != 64)
            throw new ArgumentException("A complete migration source digest is required.", nameof(sourceDigest));
        lock (Gate)
        {
            return _store.InTransaction((db, tx) =>
            {
                var importId = FranchiseHubStateStore.SqlValue(db, tx,
                    "SELECT import_id FROM legacy_import_journal WHERE source_digest=$digest",
                    ("$digest", sourceDigest));
                if (importId is null)
                    return new LegacyRollbackResult(true, 0, "", sourceDigest);
                var backup = FranchiseHubStateStore.SqlValue(db, tx,
                    "SELECT backup_file FROM legacy_import_journal WHERE import_id=$id",
                    ("$id", importId)) ?? "";
                var unsafeRows = FranchiseHubStateStore.SqlValue(db, tx, @"
 SELECT COUNT(*) FROM journey_progress WHERE migration_id=$id
 AND (state_version<>0 OR active_owner_lease IS NOT NULL)",
                    ("$id", importId));
                if (unsafeRows != "0")
                    throw new InvalidOperationException(
                        "Migrated journeys have changed after import; rollback requires a manual review.");
                var affected = int.Parse(FranchiseHubStateStore.SqlValue(db, tx,
                    "SELECT COUNT(*) FROM journey_progress WHERE migration_id=$id",
                    ("$id", importId)) ?? "0",
                    System.Globalization.CultureInfo.InvariantCulture);
                FranchiseHubStateStore.SqlExec(db, tx, @"
 DELETE FROM journey_completed WHERE EXISTS(
 SELECT 1 FROM journey_progress p WHERE p.migration_id=$id
 AND p.user_id=journey_completed.user_id
 AND p.franchise_id=journey_completed.franchise_id
 AND p.order_id=journey_completed.order_id)",
                    ("$id", importId));
                FranchiseHubStateStore.SqlExec(db, tx,
                    "DELETE FROM journey_progress WHERE migration_id=$id", ("$id", importId));
                FranchiseHubStateStore.SqlExec(db, tx,
                    "DELETE FROM legacy_progress_records WHERE import_id=$id", ("$id", importId));
                FranchiseHubStateStore.SqlExec(db, tx,
                    "DELETE FROM legacy_import_journal WHERE import_id=$id", ("$id", importId));
                return new LegacyRollbackResult(false, affected, backup, sourceDigest);
            });
        }
    }

    /// <summary>
    /// Apply is explicit and synchronous. All original staged JSON is immutable,
    /// a checksummed ZIP is created first, and the SQL change is one transaction.
    /// Missing/ambiguous keys are staged as pending_review, never counted complete.
    /// </summary>
    public LegacyMigrationResult Apply(PluginConfiguration configuration,
        string expectedDigest, byte[]? configurationBackup = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(expectedDigest))
            throw new ArgumentException("A preview source digest is required.", nameof(expectedDigest));
        lock (Gate)
        {
            var source = ReadStaged();
            if (!string.Equals(expectedDigest, source.Digest, StringComparison.Ordinal))
                throw new InvalidOperationException("The staged legacy files changed after preview.");
            if (source.Files.Count == 0) throw new InvalidDataException("No legacy progress records were staged.");

            var already = _store.InTransaction((db, tx) =>
                FranchiseHubStateStore.SqlValue(db, tx,
                    "SELECT backup_file FROM legacy_import_journal WHERE source_digest=$digest",
                    ("$digest", source.Digest)));
            if (already is not null)
                return new LegacyMigrationResult(true, 0, 0, already, source.Digest);
            var backup = CreateBackup(source, configurationBackup);
            VerifyBackup(Path.Combine(_root, "franchise-hub-state", "migration-backups", backup), source.Digest);
            var importId = Id(Guid.NewGuid());
            // One atomic transaction. Interrupted SQL never partially seeds users.
            return _store.InTransaction((db, tx) =>
            {
                var imported = 0; var pending = 0;
                var mappings = source.Files.Select(file => (File: file,
                    Mapping: Map(db, tx, configuration, file))).ToArray();

                FranchiseHubStateStore.SqlExec(db, tx, @"INSERT INTO legacy_import_journal
 (import_id,source_digest,backup_file,imported_records,pending_records,applied_utc)
 VALUES($id,$digest,$backup,0,0,$now)",
                    ("$id", importId), ("$digest", source.Digest), ("$backup", backup),
                    ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                foreach (var (file, mapping) in mappings)
                {
                    var reason = mapping.Reason;
                    var status = reason is null ? "migrated" : "pending_review";
                    if (reason is null)
                    {
                        FranchiseHubStateStore.SeedMigratedProgress(db, tx, file.UserId,
                            mapping.FranchiseId, mapping.OrderId, mapping.Revision,
                            mapping.Cursor, file.Progress.PositionTicks, mapping.Completed, importId);
                        imported++;
                    }
                    else pending++;
                    FranchiseHubStateStore.SqlExec(db, tx, @"INSERT INTO legacy_progress_records
 (record_id,source_digest,user_id,row_id,raw_sha256,raw_json,status,review_reason,import_id,imported_utc)
 VALUES($id,$digest,$user,$row,$sha,$json,$status,$reason,$import,$now)",
                        ("$id", Sha(Encoding.UTF8.GetBytes(importId + "|" + file.Relative + "|" + file.Digest))),
                        ("$digest", Sha(Encoding.UTF8.GetBytes(file.Relative + "|" + file.Digest))),
                        ("$user", Id(file.UserId)), ("$row", Id(file.RowId)),
                        ("$sha", file.Digest), ("$json", file.JsonText),
                        ("$status", status), ("$reason", reason),
                        ("$import", importId), ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                }
                FranchiseHubStateStore.SqlExec(db, tx, @"UPDATE legacy_import_journal
 SET imported_records=$imported,pending_records=$pending WHERE import_id=$id",
                    ("$imported", imported), ("$pending", pending), ("$id", importId));
                return new LegacyMigrationResult(false, imported, pending, backup, source.Digest);
            });
        }
    }
}
