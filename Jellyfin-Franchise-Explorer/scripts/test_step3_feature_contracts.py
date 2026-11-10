#!/usr/bin/env python3
"""Phase-3 static compatibility guards. No Jellyfin host or network is required.

These checks are source-level tripwires, not proof of correct live playback.
When replacing subsystems in phases 4–14, replace tripwires with behavioral
tests instead of bypassing the protected requirements.
"""
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
root = repo / "Jellyfin-Franchise-Explorer"
src = root / "src/Jellyfin.Plugin.FranchiseExplorer"
fixture = json.loads((root / "tests/fixtures/parity/legacy_v1_two_franchises.json").read_text(encoding="utf-8"))
assert fixture["schemaVersion"] == 1 and len(fixture["Nodes"]) == 3 and len(fixture["Rows"]) == 4
ids = [x["Id"] for group in ("Nodes", "MediaLinks", "Rows") for x in fixture[group]]
assert len(set(ids)) == len(ids), "Golden IDs must be unique"
assert len(fixture["Rows"][0]["MediaOrder"]) == 5

# Assertions protect *capabilities*, not the current version's naming.
requirements = {
    "virtual hierarchy / scoped rows": [
        ("Configuration/PluginConfiguration.cs", ["ConfigurationRevision", "List<FranchiseNode> Nodes", "List<MediaLink> MediaLinks", "List<FranchiseRow> Rows"]),
        ("Models/FranchiseNode.cs", ["ParentId", "NodeType", "CollectionId", "EpisodeFormat", "Artwork"]),
        ("Models/FranchiseRow.cs", ["RowType", "NodeIds", "MediaLinkIds", "List<FranchiseRowEntry> Items", "MediaOrder"]),
        ("Models/FranchiseRowEntry.cs", ["JellyfinItemId", "RangeMode", "StartSeason", "EndEpisode"]),
    ],
    "configuration draft/export and manual control": [
        ("Configuration/franchise-settings-core.js", ["normalize(raw)", "rangeFields", "categoryForRow", "scopedJobs"]),
        ("Configuration/configPage.html", ['id="export"', 'id="import"', 'id="save"']),
        ("Configuration/franchise-settings.js", ["render", "remember"]),
    ],
    "viewer-facing catalog and embedded browser": [
        ("Api/FranchiseBrowseController.cs", ['[Route("FranchiseExplorer")]', '[HttpGet("Browse")]', '[HttpGet("Catalog")]', '[Authorize]', "PlaybackExclusions"]),
        ("Configuration/browsePage.html", ["FranchiseExplorer/Catalog", "FranchiseExplorer/Browse"]),
    ],
    "verified timeline progress": [
        ("Api/FranchiseTimelineController.cs", ['[Route("FranchiseExplorer/TimelineState")]', '[HttpPut("{rowId:guid}")]', '/Activate', '/Complete', "RecordPlaybackAsync", "RemapItemIdsAsync"]),
        ("Web/TimelinePlaybackMonitorService.cs", ["PlaybackStart", "PlaybackProgress", "PlaybackStopped", "CompletedAtEndDespiteUnflaggedStop"]),
        ("Web/franchise-timeline.js", ["async resolve(row)", "async playHere(", "async verifyStarted(", "makeState(row,queue", "completedKeys"]),
    ],
    "homepage injector and web player handoff": [
        ("Web/HomeTileRegistrationService.cs", ["RegisterScript", "TryRegister", "ScriptResourceName", "TimelineResourceName"]),
        ("Web/franchise-my-media.js", ["Continue Your Franchises", "FranchiseExplorerNativePlayback", "FranchiseExplorer/TimelineState", "FranchiseExplorer/Catalog"]),
    ],
    "automation and recovery": [
        ("Api/FranchiseAutoAddController.cs", ['[Authorize(Policy = Policies.RequiresElevation)]', 'HttpGet("Source")']),
        ("Api/FranchiseAutomationController.cs", ['[Authorize(Policy = Policies.RequiresElevation)]', 'HttpPost("Preview")', 'HttpPost("Decision")', 'HttpPost("Exclusion")']),
        ("Services/AutoFranchiseService.cs", ["PreviewAsync", "RunAsync", "RequireFingerprint", "TryCanonicalListUrl"]),
        ("Tasks/AutoFranchisesTask.cs", ["DailyTrigger", "StartupTrigger"]),
        ("Api/FranchiseRecoveryController.cs", ['[Authorize(Policy = Policies.RequiresElevation)]', 'HttpPost("Apply")']),
        ("Services/MediaRecoveryService.cs", ["CandidatesAsync", "ApplyAsync", "ConfigurationRevision"]),
    ],
    "artwork / subtitles / per-user authorization": [
        ("Api/FranchiseArtworkController.cs", ["DetectImage", "MaxImageBytes", "VideoPreview", "AllowedSlots", "RequiresElevation"]),
        ("Api/FranchisePlaybackPreferencesController.cs", ["OwnsDevice", "PlaybackPreferencesRequest", "subtitleStreamIndex"]),
        ("Web/PlaybackSubtitlePolicy.cs", ["PlaybackSubtitlePolicyStore", "OwnsDevice", "SubtitleLanguagePreference"]),
        ("Api/FranchiseTimelineController.cs", ["TryUser", "PathFor(user"]),
    ],
}
checked = 0
for feature, checks in requirements.items():
    for relative, markers in checks:
        source = (src / relative)
        assert source.is_file(), f"{feature}: missing {relative}"
        content = source.read_text(encoding="utf-8")
        for marker in markers:
            assert marker in content, f"{feature}: missing {marker!r} in {relative}"
            checked += 1

# Browser catalog must never publish admin-only display labels as trusted visible metadata.
catalog = (src / "Api/FranchiseBrowseController.cs").read_text(encoding="utf-8")
assert "l.DisplayName" not in catalog and "i.DisplayName" not in catalog
# Only a distinct plugin may be published from this repository, and only after its release gate is implemented.
identity = json.loads((repo / "project-identity.json").read_text(encoding="utf-8"))
assert identity["pluginId"] != "6bc7820b-7c6d-4e64-a139-dfb90b8d6f42"
assert identity["repository"] == "shuach613/jellyfin-franchise-hub"
assert json.loads((repo / "manifest.json").read_text(encoding="utf-8"))[0]["versions"] == []
print(f"PASS Step 3 feature contracts: {len(requirements)} feature groups, {checked} source markers, legacy fixture and viewer-data safeguards")
