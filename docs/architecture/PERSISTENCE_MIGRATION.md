# Transactional storage, migrations and legacy compatibility — design

**Step 4 design only.** No SQLite data store, migration, import/export changes or new endpoints exist yet. The existing configuration and user progress remain untouched until implementation Steps 6–7.

## Storage split and persistence ownership

- Franchise nodes, existing MediaLinks, Rows, source/import settings, artwork references, admin navigation label and configuration revision stay in Jellyfin plugin configuration XML during initial migration. A new canonicalOrderId/primaryChronologyRowId mapping may be introduced as additive compatible fields in Step 5–6.
- Plugin-owned artwork bytes remain in their existing folder until verified package backup and asset manifest migration.
- SQLite under Franchise Hub's own Jellyfin plugin data directory owns **only durable new-domain state**: sequence snapshots, slot/occurrence identities, per-user chronology progress, isolated session positions, verified event receipts, media remapping, and schema migrations.
- Never modify Jellyfin library database, actual media files, or its watched-status history directly.

## Proposed SQLite tables and constraints

| Table | Keys / invariants |
| --- | --- |
| schema_migrations | version PK, checksum, applied UTC; no forward version downgrade |
| watch_order_registry | order_id PK, franchise_id, canonical flag, sourceRowId; at most one canonical order per franchise |
| sequence_revisions | (order_id, revision) PK; immutable hash and timestamp |
| ordered_slots | (order_id, slot_id) PK, legacyReferenceId, definition; stable across reorder |
| occurrence_registry | occurrence_id PK; order_id, slot_id, logicalMediaKey, ordinal; validated unique association |
| sequence_snapshots | snapshot_id PK; order_id, revision, viewer_user_id, permissionDigest, immutable payload, expiry |
| journey_progress | (user_id, franchise_id, order_id) PK; cursor occurrence, ticks, state_version, active_owner_lease |
| journey_completed | (user_id, franchise_id, order_id, occurrence_id) PK; completion evidence and timestamp |
| playback_sessions | plugin_session_id PK; user_id, mode, snapshot, expected occurrence, actual Jellyfin session, playbackInstanceId, status and lease |
| playback_event_receipts | receipt_id PK; instance, event kind, expected occurrence, observed item, ordering token; unique completion receipt per playback instance and occurrence |
| media_identity_mappings | before ID, verified after ID, provider identities, approval status, last validated UTC |
| import_migration_journal | source GUID, version, backup location reference, validation state and checksum |

Run WAL only on a filesystem where SQLite locking semantics are safe; support busy timeout, single-writer transaction and tested restart recovery. Avoid placing SQLite databases on unreliable network shares. Keep large snapshots bounded by configured limits and retention; never prune a still-referenced progress revision.

## Transactional write rules

1. For tracked playback, claim one active canonical lease per (userId, franchiseId) and bind to the exact queue snapshot and playback instance. A second tab must request takeover.
2. The event monitor correlates **server-observed** Jellyfin user, device/session and media ID to the plugin's expected start. Until verified, a prepared/dispatching session cannot advance progress.
3. On completion, check receipt deduplication and current lease+occurrence in one transaction. Store completion receipt, add completed occurrence if absent, move cursor to next occurrence, increment state version, and publish a pending next-item intent atomically.
4. Duplicate, stale, unverified or unrelated events are logged as redacted diagnostics and cannot mutate canonical progress. Progress updates with ordering ambiguity require the current ownership token.
5. On shutdown/restart, pending sessions and verified state are reconciled deterministically; the default behavior is stop awaiting verification, never guess autoplay.

## Existing upstream data migration — Step 6

1. Detect old and new plugin data roots using explicit Jellyfin supported paths; never assume the new GUID automatically migrates a prior plugin folder.
2. Take immutable, checksummed backups of the original configuration XML, artwork folder and all per-user timeline-progress JSON files. Require enough free space and write permissions.
3. Parse old config and state without executing arbitrary content; validate IDs, rows, lengths, ranges, access boundaries and file paths. Backups are private to administrator; per-user data is never exposed to another user.
4. Reconstruct canonical orders from valid existing Timeline rows. One eligible row can be proposed automatically with an audit record; multiple rows require admin selection. Never change chronology without review.
5. Convert legacy queue keys (referenceId:itemId) into stable occurrences by unique verified mapping; ambiguous duplicate episode/movie mappings are stored as pending review, not guessed.
6. Import all completed occurrences, cursor position, other row histories and source link identities transactionally; record warnings and unrepresentable states for human review. Preserve historical JSON unchanged.
7. Verify counts and checksums and complete migration in one tracked transaction. On failure restore from snapshot; do not partially activate the new store.
8. Restart/replay migration on the same data must be idempotent. Explicit backup restore does not silently overwrite newer data.

## Bidirectional legacy import/export — Step 7

The original plugin's JSON format (schemaVersion 1, Nodes, MediaLinks, Rows and other supported config fields) is a separate compatibility artifact from the new plugin's full native backup:
- **Original -> new**: import v1 JSON, validate and preview, identify missing media and item-ID collisions, choose merge/replace/clone, preserve manually configured order and unknown supported fields.
- **New -> original**: reconstruct the exact original config and view-order representation. The original importer operates on an **entire editor workspace**, so a single-franchise transfer must be safely composed with the target workspace; never promise a direct partial merge the older plugin cannot perform.
- Original v1 JSON does **not** contain new playback mode/session state, uploaded image bytes or per-user progress. A compatibility report itemizes every omitted new-only field before export; fail safely on media references that cannot be represented.
- **Native ZIP backup**: manifest with schema versions, checksums and paths for config JSON/XML representation, artwork assets and optional strictly scoped per-user progress; no raw Jellyfin credentials.
- **Round-trip tests**: real unchanged upstream v0.8.9.28 importer/exporter, original golden fixture from Step 3, per-user migration tests, config-only legacy export, old and new version restore.

## Security of migration

No import writes Jellyfin library metadata. Asset file names and archive paths must be normalized and checked against traversal, oversized/decompression-bomb content, forbidden symlinks and arbitrary web URLs. Binary uploads are checked independently of extension. Never treat source franchise JSON as trusted executable JavaScript. Audit logs redact paths and identifying tokens.

Release-blocking migration tests are required in Step 17, along with a documented backup/rollback procedure. Step 4 itself does **not** run a database migration.
