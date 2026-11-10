# Step 4 — Unified Franchise and Playback Architecture

**Status:** Binding design for Steps 5–10; **not implemented in the runtime**. Step 4 changes only documents, machine-readable design fixtures, CI validation and tests. Existing plugin playback remains unchanged.

## Non-negotiable invariants

1. **Franchise Play, Homepage Continue and Resume** always use a franchise's explicitly selected **canonical chronological order**, never IMDb release date, Jellyfin native Next Up, or a stale homepage queue. **Play Chronologically** remains available wherever the user is browsing the franchise.
2. Users may choose **movies only, chronological series episodes only, a complete series, one era, a custom selection, or an explicitly configured release order**. These are **independent sessions**. They can affect native Jellyfin watched state but cannot silently advance canonical chronology progress.
3. The **server** alone resolves authoritative queues, validates exact IDs, owns session and progress state, and determines the next occurrence. Jellyfin Home and Franchise Browser only display/use the same API.
4. An HTTP 204, native Web click or browser intent is not playback confirmation. Tracking requires matching Jellyfin user, device/session, exact item and intended occurrence from observed playback.
5. User-authored chronological placements and episode mappings override IMDb/source order, release dates, and automatic season numbering.
6. Reordering and media recovery preserve history using stable occurrence identities and immutable revisions. Ambiguous remaps are flagged; no guessing.
7. Hierarchies, custom shelves, artwork, automation, subtitle preferences, recovery, viewer permissions, original backup format and license are protected by the Step 3 parity matrix.

## Target module ownership

| Component (planned) | Responsibility | Explicitly forbidden |
| --- | --- | --- |
| FranchiseDefinitionService | Existing XML hierarchy, rows, canonical row selection, config revisions | User playback progress |
| IWatchOrderResolver | Expand media/ranges, enforce user accessibility and exclusions, build immutable sequence | Direct player command and tracked progress mutation |
| ISequenceReconciler | Map stable occurrences across edits/rescans and report conflicts | Guessing ambiguous duplicates |
| IPlaybackSessionService | Prepare, launch intent, verify, take over, stop; session lifetime | Treating dispatch as successful playback |
| IPlaybackProgressStore | Atomic canonical cursor, completions and isolated session state | Native Jellyfin watched flag mutation |
| IPlaybackEvidenceService | Bind Jellyfin start/progress/stopped events to intended playback instance | Trusting client-reported completed status |
| IPlayerAdapter | Versioned Jellyfin Web local or verified remote playback | Choosing the next chronological media |
| IFranchiseCompatibilityAdapter | Upstream JSON schemaVersion 1 import/export, warnings | Silently claiming new-only data exports to upstream |
| IBackupService | Native config, assets and opt-in per-user progress, restore and rollback | Exposing another user's progress |
| Web presenters | Same API-driven Home, Browse, settings and player controls | Resolving/saving authoritative queues |

## End-to-end data flow

    Jellyfin config XML ---+----> Franchise definitions --+--> IWatchOrderResolver
                           |                              |       ^
    Jellyfin library ------+--> permissions + item IDs ----+       |
                                                                  v
                      immutable sequence: order ID + revision + occurrence IDs
                                                                  |
                             +------------------------------------+----+
                             |                                         |
                  Web Home + Browse                              Sessions API
                   SELECT mode                         prepare -> launch -> verify
                             |                                         |
                             +-----------------------> Playback coordinator
                                                                    |
                            Jellyfin verified player events --------+
                                                                    |
                                           SQLite per-user canonical progress
                                            + independent optional session state
                                                                    |
                                          Home and Browse query SAME progress

    Legacy JSON backup -> compatibility adapter -> existing franchise config
    Native backup ZIP -> validated config, artwork and opted-in progress

## Existing source mapping (integration seams)

- Config: Models/FranchiseNode.cs, FranchiseRow.cs, FranchiseRowEntry.cs, MediaLink.cs, Configuration/PluginConfiguration.cs and Plugin.cs.
- Browse API: Api/FranchiseBrowseController.cs, Configuration/browsePage.html.
- Current browser resolver: Web/franchise-timeline.js TimelineManager.resolve(row), makeState and playHere; later replaced behind a compatibility adapter.
- Current Home queue from saved state: Web/franchise-my-media.js playableQueue(state) and homepageAction('resume'); later becomes a thin API client.
- Progress: Api/FranchiseTimelineController.cs, Web/TimelinePlaybackMonitorService.cs; existing per-user JSON moved to transactional storage with a reversible migration.
- Optional integration: Web/HomeTileRegistrationService.cs; Web-only, Injector-dependent until appropriate alternative exists.
- Ancillary systems: AutoFranchiseService, MediaRecoveryService, FranchiseArtworkController, PlaybackSubtitlePolicyStore remain fully supported.

## Canonical order selection

A franchise is the top-level virtual franchise node, not a native Jellyfin collection. It may have multiple configured Timeline rows. The new franchise settings shall identify exactly ONE canonical order (primaryChronologyRowId) when possible:
- One unambiguous valid Timeline row: migration may nominate it with an explicit review record.
- Multiple eligible Timeline rows: an administrator must choose, never guess.
- No configured valid chronology: Play Chronologically remains visible but disabled with a setup explanation; release order is never silently substituted.
- Composing era-specific rows into a franchise chronology requires an explicit composition order and duplicate handling, not implicit subgroup recursion.
- Rearranging visual shelves does not alter playback sequence unless the designated canonical order itself changes.

## User action semantics

| Action | Queue source | Canonical progress |
| --- | --- | --- |
| Continue / Play Franchise | Canonical next occurrence and saved position | Tracked after verified start/completion |
| Play Chronologically from Beginning | Full canonical order | Reset requires explicit confirmation |
| Play Chronologically from Here | Canonical order from selected occurrence | Explicit tracked cursor change |
| Movies Only | Canonical entries filtered to Movie; relative chronological order retained | Never changed |
| Series Only | Canonical entries filtered to Episode; relative chronological order retained | Never changed |
| Complete Series | Selected Jellyfin series expanded with explicitly chosen season/special policy | Never changed |
| Selected Era | Canonical subset identified by configured era membership | Never changed |
| Custom Selection | Chosen movies, full series, selected episodes, ranges in user-selected order | Never changed |
| Release Order | Separately configured release-order definition (if present) | Never changed |
| Watch Only / Replay | One item | Never changed |
| Explicit Count Toward Chronology | Unambiguous selected canonical occurrence | Single atomic explicit change, review if ambiguous |

## Phase gates

Step 5 implements sequence resolution; 6 transactional storage/migration; 7 compatibility adapter; 8 verified event state; 9 Jellyfin Web adapter; 10 selective sessions. Steps 11–13 put the shared API in the UI. Steps 14–17 harden, verify, package and safely migrate.

See DOMAIN_MODEL.md, API_V1.md, PERSISTENCE_MIGRATION.md, DECISIONS.md, and ACCEPTANCE_TESTS.md. The design manifest is contracts/v1/architecture.json and is checked by scripts/test_step4_architecture_contract.py.
