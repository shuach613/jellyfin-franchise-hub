#!/usr/bin/env python3
"""Ensure the independent plugin's version/identity is consistent before packaging."""
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[1]
repository = root.parent
identity = json.loads((repository / "project-identity.json").read_text(encoding="utf-8"))
version = sys.argv[1] if len(sys.argv) > 1 else identity["version"]
if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version) or version != identity["version"]:
    sys.exit("Expected the four-component version from project-identity.json.")
project = root / "src/Jellyfin.Plugin.FranchiseExplorer"
def require(path, tokens):
    s = path.read_text(encoding="utf-8")
    for needle in tokens:
        if needle not in s:
            sys.exit(f"Missing independent identity marker in {path}: {needle}")

csproj = project / "Jellyfin.Plugin.FranchiseExplorer.csproj"
require(csproj, [f"<{key}>{version}</{key}>" for key in ("Version","AssemblyVersion","FileVersion")] + [f"<AssemblyName>{identity['assemblyName']}</AssemblyName>"])
require(project / "Plugin.cs", [identity["pluginId"], f'public override string Name => "{identity["name"]}";'])
require(project / "Web/HomeTileRegistrationService.cs", [f'CurrentScriptId = "{identity["scriptId"]}"',f'["pluginVersion"] = "{version}"',f'["pluginName"] = "{identity["name"]}"'])
require(project / "Api/FranchiseBrowseController.cs", [f'version = "{version}"'])
require(project / "Web/franchise-my-media.js", [f"window.__franchiseHubUiVersion = '{version}'",f"window.__franchiseTimelineHomeVersion = '{version}'",f"window.__franchiseAutoplayBridgeVersion='{version}'",f"setAttribute('data-fe-version','{version}')",f"CACHE_PREFIX = 'fe-franchise-home-v{version.replace('.','')}:'"])
require(project / "Configuration/configPage.html", [f'<span class="version">v{version}</span>'])
require(repository / "README.md", [version,identity["scriptId"]])
for path in (repository / "manifest.json", root / "manifest.json"):
    manifest = json.loads(path.read_text(encoding="utf-8"))
    if len(manifest) != 1 or manifest[0]["guid"] != identity["pluginId"] or manifest[0]["name"] != identity["name"] or manifest[0]["owner"] != "shuach613":
        sys.exit(f"Catalog identity is incorrect: {path}")
    for ver in manifest[0]["versions"]:
        if identity["repository"] not in ver["sourceUrl"]:
            sys.exit("Catalog references a foreign release. Publishing refused.")
release = (repository / ".github/workflows/release.yml").read_text(encoding="utf-8")
if "publishing disabled until Step 16" not in release or "exit 1" not in release:
    sys.exit("Independent release publishing is not safely disabled.")
print("Independent Franchise Hub identity/version verified:", version)
