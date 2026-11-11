# Step 5 — Authoritative chronological watch-order resolver

**Implementation status: server-side resolver and authenticated read-only preview API implemented. The existing Jellyfin player, client timeline resolver and progress files are intentionally unchanged. No new release has been published.**

## Implemented runtime components

- Models/ChronologicalWatchOrder.cs: safe, permission-scoped media DTO, catalog provider contract, resolved occurrences, warnings and response envelope.
- Services/ChronologicalWatchOrderResolver.cs: pure, side-effect-free server-owned canonical Timeline expansion.
- Services/JellyfinChronologyViewerCatalog.cs: user-scoped Jellyfin 12.2 media lookup. A non-null verified Jellyfin User is mandatory; it calls library.ConfigureUserAccess before querying. Never uses an unrestricted automation or recovery metadata snapshot to serve viewer media.
- Api/FranchiseWatchOrderController.cs: authenticated POST /FranchiseHub/v1/franchises/{franchiseId}/resolve. The request currently only accepts mode=chronological, schemaVersion=1. This is a **preview-only** operation.
- FranchiseHubServiceRegistrator.cs: registers IWatchOrderResolver in Jellyfin's dependency injection.
- Models/FranchiseNode.cs: additive PrimaryChronologyRowId field, default empty, preserving legacy XML serialization.
- tests/AutoAdd.Tests/ChronologicalWatchOrderTests.cs: C# behavioral suite wired into the existing CI test executable.

## What the resolver does

1. Verifies a real franchise root and finds Timeline rows inside its own descendants. Nested independent franchises cannot contribute rows.
2. Uses PrimaryChronologyRowId when configured; otherwise accepts **only** exactly one unambiguous Timeline row. Multiple candidates => 409 ambiguous_primary_order; none => 422 chronology_not_configured; invalid configured row => 409 invalid_primary_order.
3. Merges existing MediaLinkIds and direct row Items, preserving explicit MediaOrder and stable reference IDs. Unspecified entries retain original order; duplicate/orphan references and implicit subgroups produce warnings instead of invented media.
4. Resolves the signed-in viewer's accessible Jellyfin Movies, single Episodes, Series episode ranges and BoxSet members. Expands TV series deterministically by season/episode inside its configured slot, while **never sorting independent chronology slots by release date or series order**.
5. Honors Full, Seasons and Episodes ranges, including explicit season 0 selection and indivisible multipart files. Full series expansion defaults to numbered seasons (specials require explicit placement).
6. Applies effective franchise/row-scoped AutoFranchise exclusions for Movie, WholeShow/Series, Season, Episode and AllowedRange, including provider-based matches.
7. Emits **stable, provisional occurrence IDs** that distinguish a media file placed twice in two different slots. Reordering references does not change those IDs. If provider identity is unavailable, the physical Jellyfin ID is used as a fallback and a warning is added.
8. Returns a queue snapshot preview, config revision, ordered media, item duration/range and structured warnings. It never writes timeline state, modifies native watched flags, starts playback or changes user settings.

## Preview API example

POST /FranchiseHub/v1/franchises/{franchiseId}/resolve

    {
      "schemaVersion": 1,
      "mode": "chronological",
      "expectedOrderRevision": 23
    }

Responses have no-store cache headers and lowerCamelCase properties. A successful response includes:

    {
      "schemaVersion": 1,
      "mode": "chronological",
      "previewOnly": true,
      "isCanonicalTracking": false,
      "franchiseId": "...",
      "orderId": "...",
      "orderRevision": 23,
      "queueSnapshotId": "...",
      "items": [
        {
          "occurrenceId": "...",
          "slotId": "...",
          "jellyfinItemId": "...",
          "kind": "Movie",
          "name": "...",
          "position": 0
        }
      ],
      "warnings": []
    }

It returns typed errors for stale configuration (409), ambiguous primary chronology (409), missing chronology (422), inaccessible/unplayable queue (422), invalid input (400), and unavailable library (503). Unsupported Step 10 modes are rejected with 422 rather than silently falling back to chronological playback. API credentials/URL tokens and library paths are never emitted.

**Crucial:** This preview snapshot ID is a deterministic token, **not** a durable immutable snapshot and **cannot** be used as a playback authorization token. Actual persisted occurrence registry, snapshot retention, conflict resolution, session verification and progress tracking belong to Steps 6 and 8. The server stores no new data in this phase.

## Behavior covered by automated tests

- Movie → individual series episodes → movie → later episode interleaving as configured.
- MediaOrder overriding insertion order; stable occurrence ID across reorder; distinct occurrences when the same film appears twice via different references.
- Multiple timeline rows require explicit primary selection; unrelated franchise rows are excluded.
- Full-series sorted episodes, explicit specials, season and episode boundaries, and multipart EpisodeEnd.
- BoxSet members retaining the supplied collection order and expanding linked series.
- Row/franchise exclusion matching and safe handling of inaccessible user media.
- Missing, malformed and orphan references return warnings; cancellation works even with empty rows.
- Source-only progress untouched: no Web client queue switch, no writes or new playback session.

CI runs this suite as part of dotnet run --project tests/AutoAdd.Tests -c Release, alongside Step 3/4 tests and the original browser behavior tests.

## Remaining work and known constraints

- **Step 6:** Replace provisional hash-based IDs with a durable per-order slot/occurrence registry and immutable snapshots; reconcile existing saved per-row JSON progress to those IDs with a reversible migration.
- **Step 7:** Preserve/export legacy schemaVersion 1 JSON, and export richer native backups with artwork/progress.
- **Step 8:** Replace browser-owned progress with server-owned verified playback session state, event receipts and idempotency.
- **Step 9:** Connect Home/Browser playback to the new server resolver; validate real Jellyfin Web local/remote actions.
- **Step 10:** Movies-only, series-only, complete-series, custom and alternate order sessions; the Step 5 endpoint intentionally supports chronological only.
- **Step 11–15:** UI integration, live Jellyfin 12.2 tests, performance/permission stress, collection membership edge cases and device compatibility.

Performance limitation: The initial user-scoped adapter builds a read-only metadata view of accessible Movie/Series/Episode/BoxSet entries per preview call (max 250,000), rather than applying a persistent query cache. Later phases should optimize resolver queries and permission-cache invalidation without sacrificing viewer access checks. This preview must **not** be exposed to production clients as a playback authority until snapshot persistence and actual device verification are implemented.

## Rollback

The pre-Step 5 code stays on baseline/upstream-v0.8.9.28 and in earlier commits. Step 5 adds isolated API and service files plus one optional XML configuration field. No Jellyfin data was migrated or altered, and no production plugin package has been released.
