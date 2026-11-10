# Step 3 — Feature-parity matrix

The status **Inherited** means the feature exists in the imported source and has associated source/tests; it does *not* mean the feature has passed live Jellyfin certification. **Fragile** describes inherited features whose current architecture cannot meet our production specification yet. **Planned** is not implemented.

| ID | Feature / invariant to preserve | Current implementation / regression evidence | Status |
| --- | --- | --- | --- |
| F01 | Virtual franchise nodes and nested groups | Models/FranchiseNode.cs; scripts/test_settings_core.js | Inherited |
| F02 | Era, Phase, Saga, Character, Collection and Custom group types | Settings core; settings browser tests | Inherited |
| F03 | Editable names, descriptions, hierarchy sorting and reparenting | Admin editor, row/drag tests | Inherited |
| F04 | Independent Groups, Media, Mixed and Timeline rows | Models/FranchiseRow.cs; browser and settings tests | Inherited |
| F05 | Shared media links and direct row-local library entries | Models/FranchiseRowEntry.cs; test_full_library_row_contract.py | Inherited |
| F06 | MediaOrder controls the order of both reference kinds | test_timeline_engine.js; test_media_ordering_browser.py | Inherited |
| F07 | Individual films and whole shows, episode and season ranges | Models/MediaLink.cs; test_episode_formats.js; test_timeline_engine.js | Inherited |
| F08 | BoxSet references and one-time collection-member imports | Browse/settings code; collection connection tests | Inherited |
| F09 | User-created art overrides, inherited fallbacks, image slots | Artwork API; ArtworkTests.cs; browser artwork tests | Inherited |
| F10 | Primary, backdrop, logo, thumb and banner art slots | FranchiseArtworkController.cs; settings artwork module | Inherited |
| F11 | Authenticated image loading and supported PNG/JPEG/WebP upload | ArtworkTests.cs; artwork browser tests | Inherited |
| F12 | Short-lived authorized video art preview | FranchiseArtworkController.cs | Inherited; live test pending |
| F13 | Draft edit, undo, review and explicit save | configPage.html, settings core and settings browser tests | Inherited |
| F14 | Configuration revisions/optimistic conflict protection | Plugin.cs; ConfigurationCommitTests.cs | Inherited |
| F15 | Upstream JSON configuration export and review-before-save import | Admin editor; test_settings_core.js; Step 3 golden fixture | Inherited |
| F16 | Preserve unknown legacy schema fields during normalization | test_settings_core.js; test_feature_parity_fixture.js | Inherited |
| F17 | Reusable IMDb lists and source URL validation | ImdbListConfigurationTests.cs; test_autoadd.js | Inherited |
| F18 | IMDb list order, exact provider identities and season grouping | ImdbTransportTests.cs; automation planners; test_episode_formats.js | Inherited; external API may change |
| F19 | Manual review, exclusions and ownership of automatic additions | AutomationPlannerTests.cs; AutoFranchiseService.cs | Inherited |
| F20 | Daily opt-in automation and startup catch-up | Tasks/AutoFranchisesTask.cs; AutomationRuntimeTests.cs | Inherited |
| F21 | Do not destroy edited manual references on AutoAdd refresh | Automation planner tests; test_settings_core.js | Inherited |
| F22 | Library scan/missing-ID detection and safe recovery candidate selection | MediaRecoveryService.cs; MediaRecoveryTests.cs | Inherited |
| F23 | Unique verified identity matching, no blind title guess | MediaRecoveryPlanner.cs; RecoveryFailureRegressionTests.cs | Inherited |
| F24 | Transactional remapping of stored franchise/timeline references | FranchiseTimelineController.cs; recovery tests | Inherited |
| F25 | Per-user watch progress distinct from Jellyfin native watched flags | FranchiseTimelineController.cs; TimelinePlaybackMonitorService.cs | Inherited; robustness work pending |
| F26 | Exact item/season/episode chronological expansion | franchise-timeline.js; test_timeline_engine.js | Fragile: browser is authoritative |
| F27 | Progress cursor remapping when sequence order or row references change | franchise-timeline.js; test_timeline_engine.js | Fragile |
| F28 | Explicit watch-only/replay without resetting franchise progress | franchise-timeline.js; homepage tests | Inherited; real player test pending |
| F29 | Confirm launch and completion against the intended Jellyfin session | TimelineState controller, monitor, home bridge tests | Fragile |
| F30 | Auto-next between movies and episodes and after browser navigation | Home bridge; autoplay/activation tests | Fragile |
| F31 | Remote device playback and bounded queue additions | franchise-timeline.js; test_timeline_engine.js | Inherited; live client test pending |
| F32 | Android integrated-player queue and auto-next behavior | franchise-my-media.js; Android test scripts | Inherited; native device test pending |
| F33 | Explicit subtitles/Off and inherited Jellyfin language choice | PlaybackSubtitlePolicy.cs; PlaybackSubtitleTests.cs | Inherited |
| F34 | Authenticated view of catalog and viewer media authorization | FranchiseBrowseController.cs; missing-media browser tests | Inherited |
| F35 | Optional JavaScript Injector integration with My Media tile | HomeTileRegistrationService.cs; tile browser tests | Inherited; fragile native DOM dependency |
| F36 | Continue Your Franchises card and progress controls | franchise-my-media.js; homepage browser tests | Fragile: potential stale saved queue |
| F37 | Web browser standalone route without optional injector | FranchiseBrowseController.cs; browsePage.html | Inherited |
| F38 | Responsive desktop/mobile Web layout and multilingual headings | settings/home browser tests; locale checks | Inherited; accessibility audit pending |
| F39 | Independent Franchise Hub identity and unpublished catalog | project-identity.json; check_version.py | Implemented Step 2 |
| F40 | Unchanged original plugin can import/export our selected franchise data in both directions | Step 7 roadmap only | Planned |
| F41 | Full backup package with uploaded artwork + opted-in progress | Step 7 roadmap only | Planned |
| F42 | Server-owned chronological queue with occurrence IDs and revision | Steps 4–5 roadmap only | Planned |
| F43 | Alternative movie-only, series-only and custom playback sessions isolated from chronological cursor | Step 10 roadmap only | Planned |
| F44 | Native-looking customizable library tile and menu entry | Steps 12–13 roadmap only | Planned |
| F45 | Transactional, race-safe progress and server-evidenced completed occurrences | Steps 6–8 roadmap only | Planned |
| F46 | True native client library type / cross-client Franchises tile | No supported equivalent in imported architecture | Not promised |

## Parity release rule

Before replacing an inherited system, create a behavioral test covering its documented behavior. A passing source marker alone is insufficient. In particular, preserving config fields and API route shape is separate from preserving player behavior. No upgrade may silently erase user data, artwork, excluded titles or chronology positions.

## Baseline fixture

The synthetic tests/fixtures/parity/legacy_v1_two_franchises.json contains:
- Two independent top-level franchises and one nested era.
- Two films interleaved with a series episode range and a repeated series reference for a later episode.
- A direct row entry for a second show, plus an independent movie shelf.
- Explicit MediaOrder, foreign/unknown metadata fields and range boundaries.

It intentionally contains **no real media files, credentials, personally identifying data, uploaded artwork bytes or timeline state**. It tests existing legacy configuration semantics only; it does not claim the new selective playback engine already exists.
