using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.FranchiseExplorer.Models;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.FranchiseExplorer.Services;

/// <summary>
/// Step 6 independent, plugin-owned, transactional storage. Legacy TimelineState
/// JSON and Jellyfin library/user databases are never edited by this store.
/// The DB has no network or filesystem path inputs from HTTP clients.
/// </summary>
public sealed class FranchiseHubStateStore
{
    private const int SchemaVersion = 1;
    private readonly string _path;
    private readonly object _schemaGate = new();
    private bool _initialized;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FranchiseHubStateStore(string pluginDataRoot)
    {
        if (string.IsNullOrWhiteSpace(pluginDataRoot)) throw new ArgumentException("Plugin data root required.", nameof(pluginDataRoot));
        _path = Path.Combine(Path.GetFullPath(pluginDataRoot), "franchise-hub-state", "state.sqlite3");
    }

    internal string DatabaseFile => _path;

    private static string Id(Guid id) => id.ToString("D");
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    // Include the entire viewer-visible preview (including warnings, current
    // durations and metadata) in the immutable snapshot identity. Step 5's
    // original ID alone intentionally omitted mutable warning details.
    private static Guid SnapshotIdentity(ChronologyResolution preview)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            "franchise-hub/snapshot/v1/" + preview.QueueSnapshotId.ToString("D") + "/" +
            JsonSerializer.Serialize(preview, Json)));
        digest[6] = (byte)((digest[6] & 0x0F) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
        return Guid.ParseExact($"{hex[..8]}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20, 12)}", "D");
    }

    private static SqliteCommand Cmd(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private static void Exec(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(db, tx, sql, args);
        cmd.ExecuteNonQuery();
    }
    private static string? Scalar(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(db, tx, sql, args);
        return cmd.ExecuteScalar()?.ToString();
    }

    private SqliteConnection Open()
    {
        EnsureSchema();
        return OpenUninitialized();
    }
    private SqliteConnection OpenUninitialized()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 10
        }.ToString());
        db.Open();
        Exec(db, null, "PRAGMA foreign_keys=ON;");
        Exec(db, null, "PRAGMA busy_timeout=10000;");
        return db;
    }

    private void EnsureSchema()
    {
        if (_initialized) return;
        lock (_schemaGate)
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var db = OpenUninitialized();
            // WAL is safe for a local Synology Docker volume, not for network-shared SQLite files.
            Exec(db, null, "PRAGMA journal_mode=WAL;");
            var version = int.Parse(Scalar(db, null, "PRAGMA user_version;") ?? "0", CultureInfo.InvariantCulture);
            if (version > SchemaVersion) throw new InvalidOperationException("Franchise Hub state was written by a newer plugin version; downgrade is blocked.");
            if (version == 0)
            {
                using var tx = db.BeginTransaction();
                // Never destructively modify an existing table. Only the new plugin's
                // own path may be migrated; schema_version guards mixed releases.
                Exec(db, tx, @"
CREATE TABLE IF NOT EXISTS schema_migrations(
 version INTEGER PRIMARY KEY, checksum TEXT NOT NULL, applied_utc INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS watch_orders(
 order_id TEXT PRIMARY KEY, franchise_id TEXT NOT NULL, source_row_id TEXT NOT NULL,
 is_canonical INTEGER NOT NULL CHECK(is_canonical IN (0,1)), updated_utc INTEGER NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS idx_primary_franchise ON watch_orders(franchise_id) WHERE is_canonical=1;
CREATE TABLE IF NOT EXISTS order_revisions(
 order_id TEXT NOT NULL, revision INTEGER NOT NULL, registered_utc INTEGER NOT NULL,
 PRIMARY KEY(order_id,revision), FOREIGN KEY(order_id) REFERENCES watch_orders(order_id));
CREATE TABLE IF NOT EXISTS order_slots(
 order_id TEXT NOT NULL, slot_id TEXT NOT NULL, legacy_reference_id TEXT NOT NULL,
 last_seen_revision INTEGER NOT NULL, PRIMARY KEY(order_id,slot_id),
 FOREIGN KEY(order_id) REFERENCES watch_orders(order_id));
CREATE TABLE IF NOT EXISTS occurrence_registry(
 occurrence_id TEXT PRIMARY KEY, order_id TEXT NOT NULL, slot_id TEXT NOT NULL,
 logical_media_key TEXT NOT NULL, within_slot_ordinal INTEGER NOT NULL,
 jellyfin_item_id TEXT NOT NULL, last_seen_revision INTEGER NOT NULL,
 UNIQUE(order_id,slot_id,logical_media_key,within_slot_ordinal),
 FOREIGN KEY(order_id,slot_id) REFERENCES order_slots(order_id,slot_id));
CREATE INDEX IF NOT EXISTS idx_occurrence_lookup ON occurrence_registry(order_id,slot_id,jellyfin_item_id);
CREATE TABLE IF NOT EXISTS sequence_snapshots(
 snapshot_id TEXT PRIMARY KEY, order_id TEXT NOT NULL, revision INTEGER NOT NULL,
 viewer_user_id TEXT NOT NULL, payload_sha256 TEXT NOT NULL, json TEXT NOT NULL,
 created_utc INTEGER NOT NULL, expires_utc INTEGER NOT NULL,
 FOREIGN KEY(order_id,revision) REFERENCES order_revisions(order_id,revision));
CREATE INDEX IF NOT EXISTS idx_snapshot_viewer ON sequence_snapshots(viewer_user_id,order_id);
CREATE TABLE IF NOT EXISTS journey_progress(
 user_id TEXT NOT NULL, franchise_id TEXT NOT NULL, order_id TEXT NOT NULL,
 cursor_occurrence_id TEXT NULL, position_ticks INTEGER NOT NULL DEFAULT 0 CHECK(position_ticks>=0),
 state_version INTEGER NOT NULL DEFAULT 0 CHECK(state_version>=0),
 order_revision INTEGER NOT NULL, active_owner_lease TEXT NULL,
 updated_utc INTEGER NOT NULL, migration_id TEXT NULL,
 PRIMARY KEY(user_id,franchise_id,order_id), FOREIGN KEY(order_id) REFERENCES watch_orders(order_id));
CREATE TABLE IF NOT EXISTS journey_completed(
 user_id TEXT NOT NULL, franchise_id TEXT NOT NULL, order_id TEXT NOT NULL,
 occurrence_id TEXT NOT NULL, verified_receipt_id TEXT NULL, completed_utc INTEGER NOT NULL,
 PRIMARY KEY(user_id,franchise_id,order_id,occurrence_id),
 FOREIGN KEY(occurrence_id) REFERENCES occurrence_registry(occurrence_id));
CREATE TABLE IF NOT EXISTS playback_sessions(
 session_id TEXT PRIMARY KEY, user_id TEXT NOT NULL, franchise_id TEXT NOT NULL,
 order_id TEXT NOT NULL, mode TEXT NOT NULL, snapshot_id TEXT NOT NULL,
 expected_occurrence_id TEXT NOT NULL, jellyfin_session_id TEXT NULL,
 playback_instance_id TEXT NULL, lease_id TEXT NULL, state TEXT NOT NULL,
 updated_utc INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS idx_session_user ON playback_sessions(user_id,franchise_id);
CREATE TABLE IF NOT EXISTS playback_event_receipts(
 receipt_id TEXT PRIMARY KEY, playback_instance_id TEXT NOT NULL,
 session_id TEXT NOT NULL, occurrence_id TEXT NOT NULL, kind TEXT NOT NULL,
 occurred_utc INTEGER NOT NULL, UNIQUE(playback_instance_id,occurrence_id,kind));
CREATE TABLE IF NOT EXISTS verified_media_mappings(
 id TEXT PRIMARY KEY, old_item_id TEXT NOT NULL, new_item_id TEXT NOT NULL,
 provider_identity TEXT NOT NULL, approval TEXT NOT NULL, verified_utc INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS legacy_import_journal(
 import_id TEXT PRIMARY KEY, source_digest TEXT NOT NULL UNIQUE, backup_file TEXT NOT NULL,
 imported_records INTEGER NOT NULL, pending_records INTEGER NOT NULL, applied_utc INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS legacy_progress_records(
 record_id TEXT PRIMARY KEY, source_digest TEXT NOT NULL, user_id TEXT NOT NULL, row_id TEXT NOT NULL,
 raw_sha256 TEXT NOT NULL, raw_json TEXT NOT NULL, status TEXT NOT NULL,
 review_reason TEXT NULL, import_id TEXT NOT NULL, imported_utc INTEGER NOT NULL,
 UNIQUE(import_id,source_digest),
 FOREIGN KEY(import_id) REFERENCES legacy_import_journal(import_id));
");
                Exec(db, tx, "INSERT INTO schema_migrations(version,checksum,applied_utc) VALUES(1,$h,$now)",
                    ("$h", Hash("franchise-hub-sqlite-schema-v1-beta2")), ("$now", Now()));
                Exec(db, tx, "PRAGMA user_version=1;");
                tx.Commit();
            }
            var checksum = Scalar(db, null, "SELECT checksum FROM schema_migrations WHERE version=1");
            if (checksum != Hash("franchise-hub-sqlite-schema-v1-beta2"))
                throw new InvalidOperationException("Franchise Hub SQLite schema checksum is not recognized.");
            if (Scalar(db, null, "PRAGMA quick_check;") != "ok")
                throw new InvalidOperationException("Franchise Hub SQLite integrity check failed.");
            _initialized = true;
        }
    }

    /// <summary>
    /// Persist an authorized Step 5 preview as an immutable, viewer-bound snapshot,
    /// replacing provisional hash-based occurrence IDs with durable registry IDs.
    /// Does not create/modify watch progress, and cannot start playback.
    /// </summary>
    public ChronologyResolution RegisterSnapshot(ChronologyResolution preview, Guid viewerId)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.PreviewOnly || preview.IsCanonicalTracking ||
            viewerId == Guid.Empty || preview.FranchiseId == Guid.Empty || preview.OrderId == Guid.Empty ||
            preview.QueueSnapshotId == Guid.Empty || preview.OrderRevision < 0)
            throw new ArgumentException("Expected a viewer-bound, read-only canonical preview.");
        if (preview.Items.Count > 25000) throw new ArgumentException("Snapshot exceeds size limit.");
        using var db = Open();
        using var tx = db.BeginTransaction();
        var f = Id(preview.FranchiseId); var o = Id(preview.OrderId); var user = Id(viewerId);
        Exec(db, tx, "UPDATE watch_orders SET is_canonical=0 WHERE franchise_id=$f AND order_id<>$o", ("$f", f), ("$o", o));
        Exec(db, tx, @"INSERT INTO watch_orders(order_id,franchise_id,source_row_id,is_canonical,updated_utc)
 VALUES($o,$f,$o,1,$now)
 ON CONFLICT(order_id) DO UPDATE SET source_row_id=excluded.source_row_id,
 is_canonical=1, updated_utc=excluded.updated_utc",
            ("$o", o), ("$f", f), ("$now", Now()));
        Exec(db, tx, @"INSERT OR IGNORE INTO order_revisions(order_id,revision,registered_utc)
 VALUES($o,$r,$now)", ("$o", o), ("$r", preview.OrderRevision), ("$now", Now()));

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var items = new List<ChronologyOccurrence>();
        foreach (var source in preview.Items)
        {
            if (source.SlotId == Guid.Empty || source.JellyfinItemId == Guid.Empty ||
                string.IsNullOrWhiteSpace(source.LogicalMediaKey))
                throw new ArgumentException("A resolved item lacks verified slot, media or logical identity.");
            Exec(db, tx, @"INSERT INTO order_slots(order_id,slot_id,legacy_reference_id,last_seen_revision)
 VALUES($o,$s,$ref,$rev) ON CONFLICT(order_id,slot_id) DO UPDATE SET
 last_seen_revision=excluded.last_seen_revision",
                ("$o", o), ("$s", Id(source.SlotId)), ("$ref", Id(source.ReferenceId)), ("$rev", preview.OrderRevision));
            var key = Id(source.SlotId) + ":" + source.LogicalMediaKey;
            counts.TryGetValue(key, out var ordinal);
            counts[key] = ordinal + 1;
            var identity = Scalar(db, tx, @"SELECT occurrence_id FROM occurrence_registry WHERE
 order_id=$o AND slot_id=$s AND logical_media_key=$key AND within_slot_ordinal=$ordinal",
                ("$o", o), ("$s", Id(source.SlotId)), ("$key", source.LogicalMediaKey), ("$ordinal", ordinal));
            if (identity is null)
            {
                identity = Id(Guid.NewGuid());
                Exec(db, tx, @"INSERT INTO occurrence_registry(occurrence_id,order_id,slot_id,
 logical_media_key,within_slot_ordinal,jellyfin_item_id,last_seen_revision)
 VALUES($id,$o,$s,$key,$ordinal,$item,$rev)",
                    ("$id", identity), ("$o", o), ("$s", Id(source.SlotId)),
                    ("$key", source.LogicalMediaKey), ("$ordinal", ordinal),
                    ("$item", Id(source.JellyfinItemId)), ("$rev", preview.OrderRevision));
            }
            else
            {
                // Same provider identity and slot means a rescanned physical file
                // can retain the stable occurrence. A fallback "jellyfin:" key does
                // not remap: it will be flagged for review by the Step 5 resolver.
                Exec(db, tx, @"UPDATE occurrence_registry SET jellyfin_item_id=$item,
 last_seen_revision=$rev WHERE occurrence_id=$id",
                    ("$item", Id(source.JellyfinItemId)), ("$rev", preview.OrderRevision), ("$id", identity));
            }
            items.Add(new ChronologyOccurrence
            {
                OccurrenceId = Guid.Parse(identity), SlotId = source.SlotId,
                ReferenceId = source.ReferenceId, JellyfinItemId = source.JellyfinItemId,
                SourceItemId = source.SourceItemId, LogicalMediaKey = source.LogicalMediaKey,
                Kind = source.Kind, Name = source.Name, Position = source.Position,
                Season = source.Season, Episode = source.Episode, EpisodeEnd = source.EpisodeEnd,
                RunTimeTicks = source.RunTimeTicks
            });
        }
        var result = new ChronologyResolution
        {
            FranchiseId = preview.FranchiseId, OrderId = preview.OrderId,
            OrderRevision = preview.OrderRevision,
            QueueSnapshotId = SnapshotIdentity(preview), Items = items,
            Warnings = preview.Warnings
        };
        var json = JsonSerializer.Serialize(result, Json);
        var sha = Hash(json);
        var old = Scalar(db, tx, "SELECT payload_sha256 FROM sequence_snapshots WHERE snapshot_id=$id",
            ("$id", Id(result.QueueSnapshotId)));
        if (old is not null && old != sha)
            throw new InvalidOperationException("The same snapshot ID resolved to different content; replay a new preview.");
        Exec(db, tx, @"INSERT OR IGNORE INTO sequence_snapshots(snapshot_id,order_id,revision,
 viewer_user_id,payload_sha256,json,created_utc,expires_utc)
 VALUES($id,$o,$rev,$user,$hash,$json,$now,$expires)",
            ("$id", Id(result.QueueSnapshotId)), ("$o", o), ("$rev", result.OrderRevision),
            ("$user", user), ("$hash", sha), ("$json", json), ("$now", Now()),
            ("$expires", checked(Now() + 86400)));
        tx.Commit();
        return result;
    }

    public ChronologyResolution? ReadSnapshot(Guid viewerId, Guid snapshotId)
    {
        if (viewerId == Guid.Empty || snapshotId == Guid.Empty) return null;
        using var db = Open();
        using var cmd = Cmd(db, null, @"SELECT json,payload_sha256 FROM sequence_snapshots
 WHERE snapshot_id=$id AND viewer_user_id=$user AND expires_utc>$now",
            ("$id", Id(snapshotId)), ("$user", Id(viewerId)), ("$now", Now()));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var json = reader.GetString(0);
        if (Hash(json) != reader.GetString(1))
            throw new InvalidOperationException("Stored sequence snapshot checksum mismatch.");
        return JsonSerializer.Deserialize<ChronologyResolution>(json, Json);
    }

    /// <summary>Read-only progress projection used by future Home/Browse adapters.</summary>
    public JourneyProgress? ReadProgress(Guid userId, Guid franchiseId, Guid orderId)
    {
        if (userId == Guid.Empty || franchiseId == Guid.Empty || orderId == Guid.Empty) return null;
        using var db = Open();
        using var cmd = Cmd(db, null, @"SELECT cursor_occurrence_id, position_ticks, state_version,
 order_revision, updated_utc FROM journey_progress
 WHERE user_id=$u AND franchise_id=$f AND order_id=$o",
            ("$u", Id(userId)), ("$f", Id(franchiseId)), ("$o", Id(orderId)));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var cursor = reader.IsDBNull(0) ? (Guid?)null : Guid.Parse(reader.GetString(0));
        var progress = new JourneyProgress
        {
            UserId = userId, FranchiseId = franchiseId, OrderId = orderId,
            CursorOccurrenceId = cursor, PositionTicks = reader.GetInt64(1),
            StateVersion = reader.GetInt64(2), OrderRevision = reader.GetInt64(3),
            UpdatedUtc = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4))
        };
        reader.Close();
        using var completed = Cmd(db, null, @"SELECT occurrence_id FROM journey_completed
 WHERE user_id=$u AND franchise_id=$f AND order_id=$o ORDER BY completed_utc, occurrence_id",
            ("$u", Id(userId)), ("$f", Id(franchiseId)), ("$o", Id(orderId)));
        using var rows = completed.ExecuteReader();
        var ids = new List<Guid>();
        while (rows.Read()) ids.Add(Guid.Parse(rows.GetString(0)));
        progress.CompletedOccurrences = ids;
        return progress;
    }

    /// <summary>
    /// Admin-driven legacy progress seed. Called ONLY inside a complete, validated
    /// migration transaction; never by unverified client PlaybackProgress events.
    /// </summary>
    internal static void SeedMigratedProgress(SqliteConnection db, SqliteTransaction tx,
        Guid user, Guid franchise, Guid order, long revision, Guid? cursor,
        long positionTicks, IReadOnlyCollection<Guid> completed, string importId)
    {
        var existing = Scalar(db, tx, @"SELECT state_version FROM journey_progress
 WHERE user_id=$u AND franchise_id=$f AND order_id=$o",
            ("$u", Id(user)), ("$f", Id(franchise)), ("$o", Id(order)));
        if (existing is not null)
            throw new InvalidOperationException("Existing canonical progress cannot be replaced by a legacy import.");
        Exec(db, tx, @"INSERT INTO journey_progress(user_id,franchise_id,order_id,cursor_occurrence_id,
 position_ticks,state_version,order_revision,active_owner_lease,updated_utc,migration_id)
 VALUES($u,$f,$o,$cursor,$ticks,0,$rev,NULL,$now,$import)",
            ("$u", Id(user)), ("$f", Id(franchise)), ("$o", Id(order)),
            ("$cursor", cursor is null ? null : Id(cursor.Value)),
            ("$ticks", positionTicks), ("$rev", revision),
            ("$now", Now()), ("$import", importId));
        foreach (var occurrence in completed.Distinct())
        {
            Exec(db, tx, @"INSERT INTO journey_completed(user_id,franchise_id,order_id,
 occurrence_id,verified_receipt_id,completed_utc) VALUES($u,$f,$o,$id,NULL,$now)",
                ("$u", Id(user)), ("$f", Id(franchise)), ("$o", Id(order)),
                ("$id", Id(occurrence)), ("$now", Now()));
        }
    }

    internal T InTransaction<T>(Func<SqliteConnection, SqliteTransaction, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var db = Open();
        using var tx = db.BeginTransaction();
        var result = action(db, tx);
        tx.Commit();
        return result;
    }

    internal static string? SqlValue(SqliteConnection db, SqliteTransaction tx, string sql, params (string, object?)[] args)
        => Scalar(db, tx, sql, args);
    internal static void SqlExec(SqliteConnection db, SqliteTransaction tx, string sql, params (string, object?)[] args)
        => Exec(db, tx, sql, args);

    /// <summary>SQLite's consistency check; deliberately not an HTTP action.</summary>
    public bool IntegrityCheck()
    {
        using var db = Open();
        return Scalar(db, null, "PRAGMA integrity_check;") == "ok";
    }

    public int DatabaseSchemaVersion
    {
        get
        {
            using var db = Open();
            return int.Parse(Scalar(db, null, "PRAGMA user_version;") ?? "0", CultureInfo.InvariantCulture);
        }
    }
}

public sealed class JourneyProgress
{
    public Guid UserId { get; init; }
    public Guid FranchiseId { get; init; }
    public Guid OrderId { get; init; }
    public Guid? CursorOccurrenceId { get; init; }
    public long PositionTicks { get; init; }
    public long StateVersion { get; init; }
    public long OrderRevision { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public IReadOnlyList<Guid> CompletedOccurrences { get; set; } = Array.Empty<Guid>();
}
