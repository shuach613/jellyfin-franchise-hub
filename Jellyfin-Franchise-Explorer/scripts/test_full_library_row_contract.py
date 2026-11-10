#!/usr/bin/env python3
"""Current full-library row feature contract (Step 3 source-level baseline).

The historical test used old admin editor selectors no longer present in 0.8.9.28.
Validate the real source modules and preservation invariants instead.
"""
from pathlib import Path
project=Path(__file__).resolve().parents[1]/'src/Jellyfin.Plugin.FranchiseExplorer'
admin=(project/'Configuration/configPage.html').read_text(encoding='utf-8')
controller=(project/'Api/FranchiseBrowseController.cs').read_text(encoding='utf-8')
browse=(project/'Configuration/browsePage.html').read_text(encoding='utf-8')
row=(project/'Models/FranchiseRow.cs').read_text(encoding='utf-8')
entry=(project/'Models/FranchiseRowEntry.cs').read_text(encoding='utf-8')
assert 'List<FranchiseRowEntry> Items' in row
assert 'MediaLinkIds' in row and 'MediaOrder' in row and 'NodeIds' in row
for field in ('JellyfinItemId','ItemType','RangeMode','StartSeason','EndSeason','StartEpisode','EndEpisode'):
    assert field in entry,field
assert "includeItemTypes: types.join(',')" in admin, 'Admin must derive library search item types'
assert "includeItemTypes: episodes ? 'Movie,Series,Episode,BoxSet' : 'Movie,Series,BoxSet'" in admin
assert 'target.row.Items.push(' in admin, 'Direct library entries must be preserved'
assert 'items = r.Items?.Select' in controller and 'mediaOrder' in controller
assert 'rowIsMedia(r)' in browse and 'mediaRank' in browse
assert 'i.DisplayName' not in controller and 'l.DisplayName' not in controller, 'Viewer catalog may not expose private admin labels'
print('PASS full-library row references, mixed ordering, search, range fields and viewer-safe catalog (source contract)')
