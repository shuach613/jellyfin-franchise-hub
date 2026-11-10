# Domain model and playback invariants — design contract v1

## Persistent identities

- **franchiseId:** stable UUID of the franchise root.
- **orderId:** stable UUID of an order definition, e.g. main chronology vs optional release order.
- **primaryChronologyRowId:** explicit legacy row backing the primary order.
- **orderRevision:** monotonically increasing integer; separate SHA-256 input fingerprint includes order slots, exclusions, ranges and relevant library identity.
- **slotId:** stable persisted UUID for an ordered placement. A title intentionally appearing twice has different slots, even if both point at the same original reference.
- **occurrenceId:** stable persisted UUID of a specific playable movie/episode within ONE order slot. Generated once on first resolution and retained across moves/renames/media-ID remaps.
- **logicalMediaKey:** verified provider identity and, for episodes, parent-series provider identity + season/episode coordinates, including multipart metadata.
- **jellyfinItemId:** actual physical media ID used for playback in the current library, subject to safe remapping.
- **queueSnapshotId:** immutable UUID for a specific resolved order revision, item mapping and viewer permissions.
- **sessionId:** plugin session UUID, separate from Jellyfin session ID.
- **playbackInstanceId:** UUID per actual play attempt, including replay of the same media; completion receipts use this to avoid collision with earlier playback.
- **userId:** derived only from authenticated Jellyfin claims.

Do not identify an occurrence by current array index, Jellyfin media ID, episode number or referenceId:itemId alone. Identity registry is stored transactionally, not reconstructed arbitrarily on each page load.

### Example — repeated movie

Chronology: Movie A (slot X -> occurrence A1), S1E1 (slot Y -> E1), Movie A again (slot Z -> A2), S1E2 (slot Y -> E2). Even if both A1 and A2 have the same physical Jellyfin media ID, A1 and A2 are independently trackable. Changing slot positions must not merge completions. After a scan changes the physical media ID, a uniquely verified provider identity retains the same occurrences.

## Immutable sequence snapshots

Each viewer-specific resolution records exact occurrence order, order ID/revision, verified physical Jellyfin IDs, duration if available, item type, source scope, range boundaries, user visibility and structured warnings. Missing media or permission failures are not silently replaced with different media. Server pages through large collections with bounded concurrency, deterministic ordering and explicit maximum queue size.

An edit creates a new revision, leaving prior snapshots available for migration and concurrent sessions. Reconciliation first uses exact occurrence IDs, then unambiguous slot+logical-media matching. For bundled-series -> individual-singleton conversion, match the full sequence only if uniqueness is demonstrated; a repeated episode or unclear provider identity demands review. Preserve old progress until approved.

Full Series expansions default to numbered seasons by season/episode index (historical behavior) and make Specials/season zero a visible option. Explicit season-zero placement in *configured canonical chronology* is always honored. Multipart media files remain one playable item with documented episode span; do not invent half-file playback or duplicate unsupported playable units.

## Modes and progress isolation

The **canonical** session is the ONLY session allowed to advance franchise journey progress automatically. Filtered and standalone sessions have their own cursor, queue and optional resume state. They never mutate canonical cursor or completed occurrence IDs from routine native playback events. Jellyfin's independent native watched flags may change normally.

User progress key is (authenticatedUserId, franchiseId, primaryOrderId), including cursorOccurrenceId, completed occurrence IDs, positionTicks, stateVersion, orderRevision and last verified playbackInstanceId. Completion is a **set** keyed by occurrence, not a scalar count. Homepage and browser are read-only projections of the same persisted state.

An explicit Count Toward Chronology action is not automatic reconciliation: require a uniquely identified canonical occurrence, current configuration revision, user intent and an atomic transaction. Ambiguous duplicates are not auto-chosen.

## Race, failure, and permission contracts

- Prepare creates a time-limited, signed server-side launch intent and immutable queue snapshot; no progress committed yet.
- Player command dispatch produces an acknowledgement but not a verified start. Verify authenticated user, Jellyfin session, expected item and playback instance before making a tracked session active.
- Start/Progress/Stop may arrive twice, late or out of order. Process transactionally, use event receipts for idempotency; stale events cannot overwrite newer state.
- Explicit takeover of the same active canonical journey revokes the prior session's progress lease. Independent sessions can coexist without ownership of main chronology.
- A mismatch of expected stateVersion/orderRevision returns 409 conflict with current preview; validation uses stable error codes.
- Access changes invalidate unsafe future launches. The resolver is permission-aware and rechecks at play and auto-next, rather than trusting previously rendered client media.
- Browsing, displaying Home, fetching previews and opening settings do not update watch state.
- Remote clients without reliable auto-next have an honest capability report and safe manual-next fallback, never an arbitrary unrelated title.

The domain is deliberately separate from Web DOM implementation and from native Jellyfin watched history.
