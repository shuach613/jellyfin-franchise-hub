# Franchise Hub API v1 — proposed HTTP contract

**Design specification, not an existing endpoint.** All new routes use /FranchiseHub/v1/ and coexist temporarily with the old /FranchiseExplorer endpoints only on an isolated development installation. The unmodified original plugin cannot be simultaneously installed due to shared legacy routes, browser globals and Injector hooks.

## Authentication, viewer scope and semantics

- Every viewer operation requires an authenticated Jellyfin principal; never accept a caller-controlled userId as authority. Admin configuration and import/export mutation require Jellyfin elevation.
- JSON responses use lower camelCase and include schemaVersion: 1. Return Cache-Control: no-store for progress, queue and session endpoints. Error bodies have {schemaVersion, code, message, requestId, details?}; do not leak secret library paths.
- Request inputs have explicit limits: capped selection length, playable occurrences and request size. Return typed missing/forbidden item warnings, not fabricated playable IDs.
- API versions are **not** identical to plugin assembly versions. v1 can survive later plugin point releases without breaking clients.

## Endpoints and side effects

| Method / endpoint | Purpose | State writes |
| --- | --- | --- |
| GET /FranchiseHub/v1/franchises/{franchiseId}/orders | Canonical order ID, available alternate definitions, primaryChronologyRowId, revision, viewer capabilities | None |
| GET /FranchiseHub/v1/franchises/{franchiseId}/progress | Authoritative canonical cursor, current media, completed count, revision, resume ticks | None |
| POST /FranchiseHub/v1/franchises/{franchiseId}/resolve | Mode, optional selection and order revision -> immutable permission-filtered queue snapshot + warnings | Cache immutable snapshot, never watch progress |
| POST /FranchiseHub/v1/playback/sessions | Prepare a launch from an existing snapshot with expected revision/version and exact start occurrence | Create pending plugin session and short-lived launch intent, no cursor progression |
| POST /FranchiseHub/v1/playback/sessions/{sessionId}/launch | Initiate local adapter handoff or remote command, give expected item ID | Mark command dispatched / awaiting verification, NOT playing |
| GET /FranchiseHub/v1/playback/sessions/{sessionId} | View verified playback status and next-item decision | None |
| POST /FranchiseHub/v1/playback/sessions/{sessionId}/cancel | Cancel pending or active plugin session and revoke owner | Close plugin session, preserve canonical completed set |
| POST /FranchiseHub/v1/playback/sessions/{sessionId}/takeover | Explicit owner switch, optionally position preserving | Transactionally revoke old lease and prepare new playback instance |
| POST /FranchiseHub/v1/franchises/{franchiseId}/progress/restart | Explicit confirmed reset/reposition in canonical order, If-Match expected state revision | Atomic canonical reset/reposition only |
| POST /FranchiseHub/v1/franchises/{franchiseId}/progress/credit | Explicit mark a uniquely selected canonical occurrence completed | Atomic single occurrence credit; ambiguous -> 409 |
| POST /FranchiseHub/v1/franchises/{franchiseId}/progress/reconcile | Admin/viewer review of ambiguous edit mappings, with permissions and version checks | Atomic approved mapping only |
| GET /FranchiseHub/v1/diagnostics | Redacted plugin ABI, integration capability flags and safe error counts | None |

Future Step 7 endpoints include admin-only compatibility preview/import/export and full native backup transfer; they are explicitly **not** part of the core playback API.

## Request samples (conceptual)

Resolve:
    POST .../franchises/{franchiseId}/resolve
    { "schemaVersion":1, "mode":"movies_only", "expectedOrderRevision":7, "selection":null }

Response:
    {
      "schemaVersion":1, "queueSnapshotId":"<uuid>", "franchiseId":"<uuid>",
      "orderId":"<uuid>", "orderRevision":7, "mode":"movies_only",
      "items":[{"occurrenceId":"<uuid>","slotId":"<uuid>","jellyfinItemId":"<uuid>","kind":"Movie","position":0}],
      "warnings":[], "isCanonicalTracking":false
    }

Prepare:
    POST .../playback/sessions
    {
      "schemaVersion":1, "franchiseId":"<uuid>", "queueSnapshotId":"<uuid>",
      "mode":"chronological", "startOccurrenceId":"<uuid>",
      "expectedOrderRevision":7, "expectedProgressVersion":12,
      "deviceId":"<owned-device-id>", "intent":"resume"
    }

Response:
    {
      "schemaVersion":1, "sessionId":"<plugin-session-uuid>",
      "state":"prepared", "expectedJellyfinItemId":"<uuid>",
      "expectedOccurrenceId":"<uuid>", "isCanonicalTracking":true
    }

Launch:
    POST .../playback/sessions/{sessionId}/launch
    { "schemaVersion":1, "client":"jellyfin_web" }

Response:
    { "schemaVersion":1, "state":"awaiting_verification", "expectedJellyfinItemId":"<uuid>" }

**Not** a claim that playback started. Only a server-observed matching Jellyfin PlaybackStart event can change state to playing. A Web-local adapter may receive the launch intent and perform the actual local action; the server cannot invent success.

## Errors, concurrency and validation

- 400 invalid_request: malformed or unsupported request field.
- 401 unauthenticated: no verified Jellyfin principal.
- 403 forbidden: item/device/session not owned or visible to this user.
- 404 not_found: franchise/order/session absent or belongs to another user (avoid leakage).
- 409 stale_revision: expected order/progress version no longer current; current preview supplied.
- 409 ambiguous_occurrence: duplicate/migrated item cannot be uniquely credited; user review needed.
- 409 active_session_conflict: explicit takeover required before a second canonical session.
- 410 snapshot_expired: snapshot has expired or item mapping is no longer safe.
- 422 unplayable_selection: selection empty, no accessible matching items, or incompatible mode.
- 424 playback_unverified: adapter command could not verify exact expected item/session. No watched progress changed.
- 503 capability_unavailable: client-specific playback or remote control not safely available.

Server-generated requestId is for diagnostics correlation; no credentials, raw paths or tokens in error details. An unexpected 204 returned by Jellyfin's remote control is never interpreted as our API's verified-playing state.

## Next-item handoff

After verified completion, the server transaction stores the next **occurrenceId** and may issue an actionable next-item command only for the current valid owner lease. An adapter may start it and must then wait for a matching verified start. A failed transition preserves completed history and leaves the next occurrence pending; it must not launch Jellyfin-native Next Up instead.

On unsupported devices the API reports manual-next required. No client may calculate its own "next item" by parsing the saved queue key's last colon.
