#!/usr/bin/env python3
"""Identity-safe Injector registration and inherited home-history contract."""
from pathlib import Path
import json
root=Path(__file__).resolve().parents[1];repo=root.parent;project=root/'src/Jellyfin.Plugin.FranchiseExplorer'
identity=json.loads((repo/'project-identity.json').read_text())
cs=(project/'Web/HomeTileRegistrationService.cs').read_text()
js=(project/'Web/franchise-my-media.js').read_text()
version=(project/'Jellyfin.Plugin.FranchiseExplorer.csproj').read_text()
for field in ('Version','AssemblyVersion','FileVersion'):
    assert f'<{field}>{identity["version"]}</{field}>' in version,field
assert f'CurrentScriptId = "{identity["scriptId"]}"' in cs
assert '"jellyfin-franchise-explorer-my-media-v' not in cs
assert 'LegacyScriptIds = Array.Empty<string>()' in cs
assert '["id"] = CurrentScriptId' in cs
assert '["pluginVersion"] = "0.1.0.0"' in cs
assert 'GetMethod("UnregisterScript"' in cs
assert cs.index('register.Invoke(null, new[] { payload })') < cs.index('unregister.Invoke(')
assert 'if (result is not bool success || !success) return false;' in cs
assert 'data-fe-version' in js and '__franchiseHubUiVersion' in js
assert "CACHE_PREFIX = 'fe-franchise-home-v0100:'" in js
assert 'confirmedEmpty = active.length === 0' in js
assert 'Loading your franchise watch orders' in js
assert 'Franchise Hub Settings' in js
assert 'Open Franchises' in js
assert 'console.warn(' in js and 'error?.message' in js
assert identity['scriptId'] in (repo/'README.md').read_text()
assert (repo/'docs/UPSTREAM_README.md').is_file()
print('Franchise Hub Injector isolation and home continuity contract passed.')
