#!/usr/bin/env python3
"""Step 5 source security/parity tripwires; behavioral cases run in C# tests.

These assertions complement, but never replace, a real Jellyfin 12.2 hosted
authorization and playback test (Steps 9/15).
"""
from pathlib import Path

repo=Path(__file__).resolve().parents[2]
p=repo/'Jellyfin-Franchise-Explorer/src/Jellyfin.Plugin.FranchiseExplorer'
resolver=(p/'Services/ChronologicalWatchOrderResolver.cs').read_text(encoding='utf-8')
catalog=(p/'Services/JellyfinChronologyViewerCatalog.cs').read_text(encoding='utf-8')
controller=(p/'Api/FranchiseWatchOrderController.cs').read_text(encoding='utf-8')
models=(p/'Models/ChronologicalWatchOrder.cs').read_text(encoding='utf-8')
registrator=(p/'FranchiseHubServiceRegistrator.cs').read_text(encoding='utf-8')
node=(p/'Models/FranchiseNode.cs').read_text(encoding='utf-8')
program=(repo/'Jellyfin-Franchise-Explorer/tests/AutoAdd.Tests/Program.cs').read_text(encoding='utf-8')
behavior=(repo/'Jellyfin-Franchise-Explorer/tests/AutoAdd.Tests/ChronologicalWatchOrderTests.cs').read_text(encoding='utf-8')

for field in ("ChronologyOccurrence","ChronologyResolution","ChronologyWarning",
              "IChronologyViewerCatalog","IsCanonicalTracking","PreviewOnly"):
    assert field in models,field
assert 'IsCanonicalTracking { get; init; } = false' in models
assert 'PreviewOnly { get; init; } = true' in models
assert 'IWatchOrderResolver' in resolver and 'ResolveAsync(' in resolver
assert 'PrimaryChronologyRowId' in resolver and 'PrimaryChronologyRowId' in node
assert 'ambiguous_primary_order' in resolver
assert 'missing_or_forbidden' in resolver
assert 'unexpanded_groups' in resolver
assert 'MediaOrder' in resolver and 'StartSeason' in resolver and 'EndEpisode' in resolver
assert 'EffectiveExclusions' in resolver and 'AllowedRange' not in resolver or 'allowedrange' in resolver
assert 'StableGuid(' in resolver and 'provisional_media_identity' in resolver
assert 'authenticatedUserId' in resolver and 'viewerCatalog.FindAsync' in resolver
assert 'new InternalItemsQuery(viewer)' in catalog
assert 'library.ConfigureUserAccess(query, viewer)' in catalog
assert 'GetItemList(query)' in catalog
assert 'GetChildren(_viewer, true, new InternalItemsQuery(_viewer)' in catalog
assert '_items.TryGetValue(x.Id' in catalog, 'Collection members must pass viewer-scoped dictionary check'
assert 'new InternalItemsQuery(null)' not in catalog
assert 'AutoFranchiseService.ReadLibrary' not in catalog
assert '[Authorize]' in controller
assert 'User.FindFirst("Jellyfin-UserId")' in controller
assert 'GetUserById(userId)' in controller
assert '[RequestSizeLimit(4096)]' in controller
assert 'ExpectedOrderRevision' in controller
assert 'preview' in controller.lower()
assert 'mode_not_implemented' in controller
assert 'IWatchOrderResolver, ChronologicalWatchOrderResolver' in registrator
assert 'IPluginServiceRegistrator' in registrator
assert 'await ChronologicalWatchOrderTests.Run();' in program
for expected in ('multipart','same Jellyfin movie','exclusion','hidden','reorder',
                 'ambiguous_primary_order','missing_reference','permission'):
    assert expected.lower() in behavior.lower(),expected

# Step 5 must not switch the Web application to a new, unverified engine.
home=(p/'Web/franchise-my-media.js').read_text(encoding='utf-8')
timeline=(p/'Web/franchise-timeline.js').read_text(encoding='utf-8')
assert 'playableQueue(state)' in home and 'async resolve(row)' in timeline
assert '/FranchiseHub/v1/playback/sessions' not in home
assert '/FranchiseHub/v1/playback/sessions' not in timeline
assert (repo/'docs/architecture/STEP5_WATCH_ORDER_ENGINE.md').is_file()
print('PASS Step 5 source contract: viewer access, read-only preview, order selection, exclusion, DI and C# behavioral test entry point')
