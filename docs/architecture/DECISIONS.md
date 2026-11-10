# Architecture decision record — Step 4

**Accepted design decisions only. None is implemented by Step 4.** Changing a decision requires a new ADR, migration impact assessment and parity tests.

| ADR | Decision | Reason |
| --- | --- | --- |
| 0001 | One server-authoritative resolver, shared by Home/Browse | Existing homepage saved queue and browser-generated queue can diverge |
| 0002 | Exactly one explicitly chosen canonical order per franchise | Avoid guessing among multiple Timeline rows or release dates |
| 0003 | Persist stable occurrence UUIDs, slot UUIDs and immutable revisions | Same Jellyfin item may occur twice; edits and scans change IDs and positions |
| 0004 | Independent mode sessions NEVER implicitly mutate canonical progress | Complete Series, movies-only and custom playlist views must not skip the main journey |
| 0005 | Keep configuration XML initially; use SQLite for transactional user/session progress | Minimize editor churn, permit idempotent playback event handling |
| 0006 | Separate dispatch, verification and completion with exact session/item checks | Neither Web DOM click nor HTTP 204 proves intended media is playing |
| 0007 | Upstream schemaVersion 1 adapter separate from native richer backup | Upstream cannot represent independent sessions, progress and uploaded asset bytes |
| 0008 | No coexistence with upstream plugin until inherited routes/browser globals are isolated | Separate GUID alone does not prevent route and Injector collisions |
| 0009 | Permission-denied or ambiguous media identity fails closed | Wrong-item playback/completion is worse than a visible error |
| 0010 | Initial production target: Jellyfin 12.2 Web | Native TV library presentation isn't implemented by Web-only Injector |
| 0011 | Incremental migration with immutable baseline and tests | Preserve AutoAdd, artwork, recovery, subtitle and editor functionality |
| 0012 | User-configured chronology outranks imported chronology and automatic TV order | Explicit film/episode interleaving is the primary product requirement |
| 0013 | Native Jellyfin watched status remains separate | Never overwrite global viewing flags to simulate a franchise timeline |
| 0014 | Empty custom/filtered selection is a visible 422, no fallback to full chronology | Never unexpectedly play the wrong media set |
| 0015 | JSON API v1 introduced beside legacy endpoints | Old UI can operate temporarily during staged migration |
| 0016 | Compatibility export reports every unsupported or omitted native feature | No silent loss of user-created new-order/session data |

**Deferred implementation:** actual C# dependency injection and storage classes (Steps 5–6); real Jellyfin session linkage and browser adapter details (Steps 8–9); true upstream file round trips (Step 7); hardware/browser integration tests (Step 15). Security: authenticated principal only, per-user data isolation, atomic concurrency, verified device ownership, no credentials/paths in logs.
