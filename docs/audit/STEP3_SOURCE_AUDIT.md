# Step 3 — Source and integration audit

Status: completed source-level assessment for the independent Franchise Hub codebase, with inherited functionality unchanged. This document describes the *existing* implementation, not the proposed redesign or guaranteed Jellyfin behavior.

## Scope and provenance

- Baseline: original Gost1o1/Jellyfin-Franchise-Explorer, commit e02be642c9a89e66cfd85f458e6fe0bbebd3714c (0.8.9.28).
- Frozen repository branch: baseline/upstream-v0.8.9.28.
- Working branches: main and develop; Step 2 changed product GUID, assembly name, displayed name, version, catalog, and Injector ID.
- Audit surface: plugin models, controllers, services, web JavaScript, embedded administration/browse pages, GitHub workflows, and pre-existing tests. Static source inspection and GitHub CI do not substitute for a real Jellyfin 12.2 playback test.
- Source tree: 8 API controllers, 6 service files, 11 models, 1 scheduled task, embedded Web integration with optional JavaScript Injector, settings modules, 1 .NET test project, and a large collection of JS/Python browser/contract tests.

## Current architecture

Jellyfin configuration XML
    -> FranchiseNode / MediaLink / FranchiseRow virtual definitions
    -> Authenticated Catalog endpoint (viewer-safe references)
    -> Browser settings/browse modules and Web home injector
    -> Client-side TimelineManager resolves real Jellyfin item IDs
    -> Local Jellyfin Web player OR remote Jellyfin Sessions API
    -> Jellyfin PlaybackStart / PlaybackProgress / PlaybackStopped events
    -> Authenticated TimelineState controller
    -> Per-user, per-timeline JSON files in plugin data folder

Additional independent services:
- AutoFranchiseService + AutoFranchisePlanner + IMDb transport: optional sources, preview, user review, ownership/exclusions, daily scheduled runs, and source-order preservation.
- MediaRecoveryService + MediaRecoveryPlanner + library snapshots: identify missing IDs and reconnect only verified unique matches; transactional timeline remapping.
- FranchiseArtworkController: uploaded art slots and short-lived authorized video-preview ticket flow.
- PlaybackSubtitlePolicyStore / PlaybackSubtitlePolicyFilter: user/session-scoped subtitle behavior.
- HomeTileRegistrationService: optional JS Injector reflection registration for Jellyfin Web My Media tile and Continue Your Franchises.

## Configuration and content model

Virtual data is independent from native Jellyfin library types. Groups may be Franchise, Era, Phase, Saga, Character, Collection, or Custom. Rows may contain subgroup links, shared media links, and direct library entries; rows have explicit cross-reference MediaOrder. Media references include Movies, Series, and BoxSets, with TV Full, Seasons, or Episodes ranges. Franchises do not move, copy, or rename media.

Collection member import is a snapshot, not live native BoxSet synchronization. User-custom artwork metadata is held in configuration; image files reside separately in plugin-owned storage. A legacy JSON export of configuration does not contain uploaded image bytes or user playback progress.

## Identity and compatibility boundaries

Our identity is Franchise Hub, v0.1.0.0, GUID 4f0ea498-68ea-42ce-a010-bd35b182299d, assembly Jellyfin.Plugin.FranchiseHub.dll. Original internal namespaces, URL routes under /FranchiseExplorer/, Web-local storage keys, and browser globals remain inherited. As a result, it is NOT currently safe to run both plugins on one Jellyfin server. Migrating the original GUID's configuration and user data is NOT achieved merely by changing GUIDs (reserved for Step 6). The independent catalog intentionally has no published binaries, and the manual release workflow fails closed until Step 16.

## Existing playback path, exact limitations

1. Chronological rows are expanded by browser JavaScript, not by an authoritative server queue service. A Series is expanded through Jellyfin's episode API; a BoxSet expands its available contents. Specials outside numbered seasons are not included in default full-series expansion.
2. The homepage stores reference-specific queue keys, current index, runtime and partially watched ticks. The homepage can reuse those saved queue snapshots independently of the current franchise row. This is a credible source of stale selection and inaccurate counts, but live reproduction on the user's server has not been performed.
3. Local playback uses an Injector Web adapter which currently synthesizes a hidden Jellyfin itemAction card/click to enter the native player. Remote playback sends commands to /Sessions, verifies NowPlayingItem, and appends queue chunks. The implementation is version-sensitive.
4. Per-user state is saved as JSON, including RowId, QueueKeys, CompletedKeys, Index, PositionTicks, PlaybackMode, session information, and pending-next marker. Jellyfin events drive updates and completion. Separate browser PUT requests and fire-and-forget event processing can compete; the existing semaphore limits overlapping filesystem writes but does not create a fully authoritative, revision-controlled event state machine.
5. Events are bound to the signed-in Jellyfin session and matching item. The service attempts to prevent false completion from seeks and near-end stops. This does not yet guarantee correct real-world auto-next across all Web player versions and devices.
6. Playing a media item independently is intended not to rewrite the active chronological journey, but the project does not yet implement the proposed unified alternative-order session model.

## High-priority findings and disposition

| Priority | Observation | Planned correction |
| --- | --- | --- |
| P0 | Homepage can use saved queue state that differs from the current chronological row | Steps 4–5, 7–9: one server-authoritative queue and cursor |
| P0 | Playback launch may be acknowledged without correct local Web player start | Steps 8–9: exact-item launch verification and rollback |
| P0 | Browser and Jellyfin event writes have no common revision/lease ownership | Steps 6–8: transactional per-user event state with idempotency |
| P0 | Independent GUID does not migrate historical configuration or progress | Steps 6–7 and 17: validated, reversible migration |
| P1 | JS Injector/hidden-card DOM hooks depend on unstable Jellyfin Web internals | Steps 9, 12–13: isolated, version-tested adapter |
| P1 | Original and new plugins still share routes/browser storage; simultaneous installation conflicts | Steps 4, 6, 13, 17: route/isolation and migration strategy |
| P1 | Same media ID can occur at multiple positions; reference keys are not durable sequence occurrence IDs | Steps 4–5: stable occurrence and revision model |
| P1 | Full Series playback cannot automatically interleave an external film between episodes | Step 5/10: explicit timeline occurrences; user-authored order wins |
| P1 | Original backup JSON omits uploaded artwork bytes and individual user timeline history | Steps 6–7: separate native complete backup and legacy-compatible export |
| P1 | Virtual tile and browser are Web-only; there is no true universal native Jellyfin library | Steps 12–13: native-looking Web presentation, documented client limits |
| P2 | Season zero / specials and missing/unnumbered episodes need explicit policy | Steps 5/10: precise episode-scope rules |
| P2 | Documentation labels and some comments retain older experimental descriptions | Steps 11–14: update UI/docs as behavior is replaced |

Findings are source-derived risks unless explicitly covered by the existing tests; they are not claims that every listed bug has been reproduced live.

## Security boundaries preserved

- Viewer catalog is authenticated and resolves media through the viewer's Jellyfin credentials. The controller intentionally avoids emitting private administrator-defined media display labels.
- Administrator automation, recovery, image modification and IMDb source actions require elevated authorization.
- Per-user timeline read/write uses a verified Jellyfin-UserId claim and user-specific storage path; client-supplied account identifiers are not trusted as identity.
- Artwork uploads validate allowable formats and size; video preview tickets are short-lived and permission-checked.
- Browser-only hiding of Jellyfin's native Continue Watching cards is presentation, not server authorization or native watch-flag deletion.
- A real security assessment must still examine concurrent sessions, preview-ticket handling, input validation, and permission changes while a long timeline is playing.

## Baseline parity decision

The existing core browsing, editor, automation, recovery, and artwork implementations should be preserved until replacement tests demonstrate equivalent behavior. Playback and progress deserve architectural replacement, not piecemeal assumptions. The current licensing is a custom personal/noncommercial draft; preserve notices and do not misrepresent the independent derivative as endorsed by upstream.

See FEATURE_PARITY_MATRIX.md for exact preserved capabilities and TEST_PLAN.md for executable regression suites, new fixtures and explicitly unverified tests.
