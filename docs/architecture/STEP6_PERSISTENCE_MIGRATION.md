# Step 6 — Transactional persistence and safe legacy migration

**Status: Implemented and tested in an unreleased development plugin.** New SQLite storage and administrator-only migration APIs exist, but legacy playback and its original JSON files remain active and unmodified. No migration has been run on a real user's Jellyfin installation. The standalone Explorer and Franchise Hub must not be installed concurrently until inherited routes and browser state are isolated.

## Implemented

- Dedicated SQLite database in the independent plugin data folder: franchise-hub-state/state.sqlite3.
- Forward-only checksummed v1 schema; local-volume WAL, foreign keys, busy timeout, integrity checks and forward-version downgrade refusal.
- Transactional watch orders, order revisions, slots, durable occurrence IDs, immutable user-bound queue snapshots, per-user progress/completion, and migration journal/review records.
- Reserved verified playback-session/event-receipt and media-remapping tables for later phases.
- Stable persisted occurrence IDs after reorder, restart or verified media-ID remap. Two instances of the same movie in separate timeline slots remain distinct. Different files with an ambiguous shared logical identity in one slot require review.
- Authenticated Step 5 preview now persists a viewer-specific immutable snapshot, but **never changes watch progress or launches playback**.
- Explicit, backup-first legacy-progress import with source digest fencing, canonical-row validation, exact occurrence mapping, one SQL transaction, idempotency and pending_review states.
- Verified private ZIP backups including SHA-256 manifest, original staged progress JSON, current configuration/artwork, and any original Explorer XML/artwork explicitly staged.
- Confirmed rollback deleting only unchanged imported progress, while keeping backups and original JSON.

Original plugin files, original plugin GUID, Jellyfin library, native watched status, config XML and old timeline JSON are not modified.

## Database tables

| Table | Purpose |
| --- | --- |
| schema_migrations | Forward-only schema version and checksum |
| watch_orders, order_revisions | Designated canonical chronology and revisions |
| order_slots, occurrence_registry | Persistent unique occurrence registry |
| sequence_snapshots | Viewer-bound immutable queue + SHA-256 checksum |
| journey_progress, journey_completed | Canonical per-user cursor and completions |
| playback_sessions, playback_event_receipts | Reserved for verified session handling in Step 8 |
| verified_media_mappings | Future approved item-ID reconciliation |
| legacy_import_journal, legacy_progress_records | Import idempotency and pending-review history |

The earlier TimelineState JSON controller remains the active playback-state implementation until Steps 8–9. The new SQLite store holds new preview/registry state and any progress explicitly imported from legacy JSON.

## Preparing an Explorer migration on a test installation

**This describes an available admin operation, not something executed on your NAS.**

1. Keep an independent backup and original Explorer export. Step 7 will supply full original-compatible franchise configuration import/export.
2. In a test Jellyfin server, make sure corresponding franchise rows and their original IDs are already in Franchise Hub's configuration. If not, imported history will be preserved for review. No guessed root GUID migration occurs.
3. Copy (never move) per-user original Explorer JSON progress into this private staging structure:

       <Franchise Hub DataFolderPath>/legacy-staging/timeline-progress/
         <user-id-without-hyphens>/<row-id-without-hyphens>.json

4. Optionally stage copies of the original plugin configuration and artwork:

       <Franchise Hub DataFolderPath>/legacy-staging/original-plugin-configuration.xml
       <Franchise Hub DataFolderPath>/legacy-staging/franchise-artwork/...

   These assets are backed up, not automatically applied to the new configuration. Current Franchise Hub XML/artwork are backed up too.

5. Each authenticated Jellyfin user should request a current chronological preview for the corresponding franchise through the server resolver. That creates the per-user snapshot needed to map legacy queue keys safely.
6. Administrator calls GET /FranchiseHub/v1/migration/preview and reviews the sourceDigest, ready, pendingReview, warnings and configurationRevision.
7. Administrator confirms via POST /FranchiseHub/v1/migration/apply with:

       {
         "schemaVersion": 1,
         "expectedSourceDigest": "<64-character-digest>",
         "expectedConfigurationRevision": 23,
         "confirmBackupAndImport": true
       }

   The server creates the backup, re-opens it and validates all listed SHA-256 checksums **before** committing progress changes in one SQLite transaction. Unmatched, duplicated or stale references become pending_review, never silently completed.
8. Repeated import of the same complete source set is a no-op. Changed staged files require a new preview. Previously imported progress is never silently overwritten.

## Backup and rollback

Backups reside inside the independent plugin data folder at franchise-hub-state/migration-backups/<UTC-and-random>.zip. They contain private per-user viewing history: restrict access to trusted administrators. They include a backup-manifest.json with every file's SHA-256, and may contain both current Hub and staged original Explorer config/artwork. Archive paths are never extracted or executed by the importer.

Rollback is available at POST /FranchiseHub/v1/migration/rollback:

       {
         "schemaVersion": 1,
         "sourceDigest": "<original-import-source-digest>",
         "expectedConfigurationRevision": 23,
         "confirmRollback": true
       }

It removes only progress introduced by the chosen import, only while every affected journey remains at state_version 0 with no active playback owner lease. It refuses to undo newer user activity. Original JSON, artwork/configuration, other users' independent progress, snapshots and private ZIP backups remain unchanged. Safe rollback can be repeated or followed by a fresh re-import. Full plugin reinstall/restore and filesystem backup recovery remain Step 17.

## Security, limitations and testing

- Authenticated previews use Jellyfin per-viewer library permissions; another user cannot read the same snapshot.
- Admin migration endpoints require Jellyfin elevation and an administrator account. HTTP bodies do not accept filesystem paths, arbitrary users or user-supplied progress.
- Staged file names and folders must be GUIDs; symlinks and oversize entries are rejected; the original plugin's folder is never guessed or scanned automatically.
- Conversion requires an exact (reference ID, physical media ID) match in a user-bound snapshot from the current configuration revision. Ambiguity, missing rows and mismatches stay in pending_review with original JSON retained.
- A migration transaction rolls back completely on SQL failure. Backups are retained. The import journal is keyed by complete source digest to support repeat runs and changed batches.
- SQLite WAL must be on a **local** Synology Docker volume, not a remote network filesystem. The SQLite provider and its native dependencies must be verified in the Step 16 packaging/release gate.
- Full original Explorer configuration import/export and native backup restore are **Step 7**. The original XML is archived here, not applied automatically.
- No normal viewer playback is writing to the SQLite progress tables yet; verified session/event updates are **Step 8**, UI cutover **Step 9**, real Jellyfin/NAS deployment tests **Steps 15–17**.

CI exercises schema creation and restart, durable occurrence IDs after reorder and physical remap, duplicated media, viewer isolation, warnings creating distinct snapshots, no progress writes on preview, verified ZIP contents, exact legacy cursor and completed history, idempotent repeat imports, pending review, rollback after newer activity, safe reimport, staging digest changes, database integrity and refusal to downgrade a future schema. These are automated tests, not certification of a real Jellyfin installation.

**Completion of Step 6 means storage and migration are implemented and tested, not that any user data has been migrated or a production plugin has been released.**
