# Step 3 — Regression and validation plan

## Frozen reference

- Branch baseline/upstream-v0.8.9.28 preserves the exact, original upstream source.
- Main/develop carry the independent Step 2 identity and subsequent changes.
- Original upstream provenance/notice: UPSTREAM.md.
- Golden legacy backup: Jellyfin-Franchise-Explorer/tests/fixtures/parity/legacy_v1_two_franchises.json.

## Automated layers

| Layer | Existing evidence | New Step 3 verification |
| --- | --- | --- |
| .NET build and models | dotnet restore/build; tests/AutoAdd.Tests | Retain as CI release prerequisite |
| Core settings/AutoAdd | scripts/test_settings_core.js, test_episode_formats.js, automation .NET tests | Golden legacy import + normalized re-export |
| Timeline queue and handoff | scripts/test_timeline_engine.js; playback, arming, activation tests | Static assertion of existing per-user playback endpoints |
| Security, privacy, media recovery | C# recovery/artwork/subtitle suites; viewer mocks | Check auth and public catalog invariants |
| Home and responsive browser | Playwright Chromium + Firefox browser mocks | Keep both engines and existing paths |
| Packaging/identity | scripts/check_repo.py; check_version.py; smoke_tests.py | Guard separate GUID, unpublished independent catalog |
| Real Jellyfin 12.2 server | Not available in GitHub mock checks | Dedicated later integration/test-environment phase |

New offline tests:
- node scripts/test_feature_parity_fixture.js — loads the actual settings core in a JavaScript VM; validates original JSON format, two franchises, nested era, mixed rows, exact reference order, TV ranges, unknown fields, nonmutation, idempotent re-import and rejection of cycles/duplicate groups.
- python3 scripts/test_step3_feature_contracts.py — maps the model/controller/service/web entry points for existing capabilities and checks that the protected privilege/data-leak safeguards are still present.
- The original feature tests remain enabled; these new tests do not modify runtime behavior.

Run from the Jellyfin-Franchise-Explorer directory:

    python3 scripts/test_step3_feature_contracts.py
    node scripts/test_feature_parity_fixture.js
    node scripts/test_settings_core.js
    node scripts/test_timeline_engine.js
    python3 scripts/test_full_library_row_contract.py
    python3 scripts/build_settings_page.py --check
    dotnet restore src/Jellyfin.Plugin.FranchiseExplorer/Jellyfin.Plugin.FranchiseExplorer.csproj
    dotnet build src/Jellyfin.Plugin.FranchiseExplorer/Jellyfin.Plugin.FranchiseExplorer.csproj -c Release
    dotnet run --project tests/AutoAdd.Tests -c Release

The CI build job runs a longer inherited source-level suite and the new Step 3 tests. The ui-browser job uses Playwright and mocks; it does not access a real Jellyfin host. Actions outcomes are identified by exact commit SHA in GitHub Actions; passing runs are evidence only for the conditions they actually exercise.

## Live integration scenarios not yet verified

1. Exact chronological resume selection after reordering, deleting or adding rows.
2. TV episode -> film -> different show automatic transition on real Jellyfin Web.
3. Closing or refreshing the Web app during active playback.
4. Concurrent browser sessions and out-of-order Jellyfin event callbacks.
5. Watch-only vs tracked chronological playback and duplicate media occurrences.
6. Reconnecting renamed/moved media in a live Jellyfin library scan.
7. Multi-user library access restrictions, access revocation while playing.
8. Desktop and mobile Web plus Android integrated-player variants.
9. Both Jellyfin Web layouts with other Injector scripts installed.
10. Independent plugin migration/upgrade and rollback with preexisting upstream data.
11. Transfer of real upstream export into new plugin and export back into unmodified upstream.
12. Separate native artwork package and per-user progress backup/restore.

These are release blockers, not claims of functionality already confirmed. Steps 4–15 must replace source-only assertions with hosted/behavioral checks as those components are implemented.

## CI failure triage and regression discipline

Source-level checks can fail after legitimate product renames when inherited tests embed upstream version strings or README screenshots. Correct the test's expectation using the independent project identity and preserved upstream README. Do not remove the underlying behavioral assertion; document and keep protected paths. A failing browser mock does not automatically demonstrate a user-facing Jellyfin failure, and a passing browser mock is not proof of actual native playback.

When adding playback modes, fixtures must distinguish the main chronological cursor from filtered/standalone session cursors. Keep a legacy exported JSON fixture unchanged for Step 7 compatibility and add native-version fixtures separately. Every future migration must preserve or explicitly report unrepresentable fields rather than silently lose data.

## Step 3 exit criteria

- Architecture inventory and risk register checked into the repository.
- Feature-parity matrix documents preserved, fragile and planned capabilities.
- Representative legacy backup fixture and executable tests added to CI.
- Existing code behavior is not altered during this phase.
- CI .NET + browser jobs report success for the Step 3 commit; any remaining external/live checks are explicitly deferred.
