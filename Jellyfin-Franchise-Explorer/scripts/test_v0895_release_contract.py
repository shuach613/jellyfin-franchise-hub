#!/usr/bin/env python3
"""Independent version and release guard plus preserved interface contracts."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[1];repo=root.parent;project=root/'src/Jellyfin.Plugin.FranchiseExplorer'
id=json.loads((repo/'project-identity.json').read_text())
csproj=(project/'Jellyfin.Plugin.FranchiseExplorer.csproj').read_text()
service=(project/'Web/HomeTileRegistrationService.cs').read_text()
script=(project/'Web/franchise-my-media.js').read_text()
browse=(project/'Configuration/browsePage.html').read_text()
config=(project/'Configuration/configPage.html').read_text()
release=(repo/'.github/workflows/release.yml').read_text()
for field in ('Version','AssemblyVersion','FileVersion'):
    assert f'<{field}>{id["version"]}</{field}>' in csproj
assert f'<AssemblyName>{id["assemblyName"]}</AssemblyName>' in csproj
assert f'CurrentScriptId = "{id["scriptId"]}"' in service
assert '["pluginVersion"] = "0.1.0.0"' in service
assert '["pluginName"] = "Franchise Hub"' in service
assert 'LegacyScriptIds = Array.Empty<string>()' in service
assert '"jellyfin-franchise-explorer-my-media-v' not in service
assert "window.__franchiseHubUiVersion = '0.1.0.0'" in script
assert "CACHE_PREFIX = 'fe-franchise-home-v0100:'" in script
assert "widget.setAttribute('data-fe-version','0.1.0.0')" in script
assert 'id="hide-native"' in config and 'id="subtitles"' in config
assert 'id="save"' in config and 'id="media-dialog"' in config
assert "TILE_ART_DATA = '__FE_TILE_ART_DATA_URL__'" in script
assert 'script.Replace("__FE_TILE_ART_DATA_URL__", url' in service
assert '.fe-resume-art.fe-resume-landscape::before' in script
assert "cover.classList.toggle('fe-resume-landscape',!usesPoster)" in script
assert "document.querySelector('.hero').classList.toggle('hidden',!selected)" in browse
assert "text($('brand-title'),'Franchise Hub')" in browse
assert "localStorage.getItem('fe-subtitles:'+this.context.serverId+':'+this.context.userId)" in (project/'Web/franchise-timeline.js').read_text()
assert 'franchise-tile-backdrop.webp' in csproj and (project/'Web/franchise-tile-backdrop.webp').is_file()
assert 'publishing disabled until Step 16' in release and 'exit 1' in release
assert 'gh release create' not in release
assert 'Franchise Hub' in (repo/'README.md').read_text()
assert (root/'docs/UPGRADE_V08915.md').is_file()
manifest=json.loads((repo/'manifest.json').read_text())
assert len(manifest)==1 and manifest[0]['guid']==id['pluginId']
assert manifest[0]['versions']==[], 'Must never ship upstream release binaries'
print('Franchise Hub independent-identity and release-disabled contract passed.')
