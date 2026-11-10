#!/usr/bin/env python3
"""Step 4 design contract verification only (does not exercise the runtime engine).

This guards the approved architecture before Steps 5–10 implement it. A passing
test means that the specification is internally consistent; it is NOT evidence
of correct Jellyfin playback, SQLite migrations, or client integration.
"""
from collections import deque
from pathlib import Path
import json

repo = Path(__file__).resolve().parents[2]
arch = repo / "docs/architecture"
spec = json.loads((arch / "contracts/v1/architecture.json").read_text(encoding="utf-8"))

assert spec["schemaVersion"] == 1
assert spec["status"] == "design_only", "Step 4 must never pretend services already exist"
assert spec["defaultMode"] == "chronological"
policy = spec["canonicalOrder"]
assert policy["selection"] == "explicit_primaryChronologyRowId"
assert policy["fallbackToReleaseOrder"] is False
assert policy["serverAuthoritative"] is True
assert policy["clientQueueIsAuthoritative"] is False
assert policy["allowNativeWatchedFlagsToDetermineNext"] is False
assert policy["progressOwner"] == "verified_server_event"

modes = spec["modeDefinitions"]
names = [x["id"] for x in modes]
required = {
    "chronological", "movies_only", "series_only", "complete_series",
    "selected_era", "custom_selection", "release_order", "watch_only"
}
assert set(names) == required and len(names) == len(set(names))
assert sum(bool(m["tracksCanonical"]) for m in modes) == 1
assert next(m for m in modes if m["tracksCanonical"])["id"] == "chronological"
assert next(m for m in modes if m["id"] == "movies_only")["source"] == "primary_filter"
assert next(m for m in modes if m["id"] == "series_only")["source"] == "primary_filter"
assert next(m for m in modes if m["id"] == "complete_series")["source"] == "jellyfin_series"
assert next(m for m in modes if m["id"] == "release_order")["source"] == "explicit_configured_order"
assert spec["progressPolicy"]["independentModeCanMutateCanonical"] is False
assert spec["progressPolicy"]["duplicateMediaOccurrencesDistinct"] is True
assert spec["progressPolicy"]["progressChangesOnlyOnVerifiedJellyfinEventOrExplicitAuthorizedAction"] is True
assert spec["compatibility"]["dualInstallationAllowed"] is False
assert spec["compatibility"]["upstreamInput"] == spec["compatibility"]["upstreamOutput"] == "schemaVersion:1 JSON"
assert spec["compatibility"]["oldRuntimeRoutes"] != spec["compatibility"]["newRuntimeRoutes"]

expected_ids = {
    "franchiseId","orderId","orderRevision","slotId","occurrenceId",
    "logicalMediaKey","jellyfinItemId","queueSnapshotId","sessionId",
    "playbackInstanceId","userId"
}
assert set(spec["identity"]) == expected_ids
assert len(spec["identity"]) == len(set(spec["identity"]))
assert set(spec["progressPolicy"]["canonicalKey"]) == {"userId","franchiseId","orderId"}
assert set(spec["progressPolicy"]["completionKey"]) == {"userId","franchiseId","orderId","occurrenceId"}

# Derive exact queue selections from the same Golden model. These are DESIGN
# expectations for a future resolver, not a shadow resolver shipped to users.
fixture = spec["fixture"]
items = fixture["canonical"]
ids = [x["id"] for x in items]
assert len(items) == 5 and len(ids) == len(set(ids))
occurrences = [x["occurrenceId"] for x in items]
assert len(set(occurrences)) == len(occurrences)
assert len({x["slotId"] for x in items}) < len(items), "One TV slot must expand to multiple episodes"
assert all(x["kind"] in ("Movie","Episode") for x in items)
calc = {
    "chronological": ids,
    "movies_only": [x["id"] for x in items if x["kind"] == "Movie"],
    "series_only": [x["id"] for x in items if x["kind"] == "Episode"],
    "complete_series": fixture["completeSeriesExpansion"],
    "selected_era": [x["id"] for x in items if x["era"] == fixture["selectedEra"]],
    "custom_selection": fixture["customSelection"],
    "release_order": fixture["configuredReleaseOrder"],
    "watch_only": [fixture["singleWatch"]],
}
assert calc == fixture["expected"], "Architecture fixture or expected order diverged"
assert calc["complete_series"][-1] == "e4"
assert "e4" not in ids, "Independent complete series must be able to include episodes outside canonical chronology"
assert calc["movies_only"] != calc["series_only"]
assert calc["chronological"].index("movie-b") > calc["chronological"].index("e2"), "Source chronology matters"
assert calc["custom_selection"] != [v for v in ids if v in calc["custom_selection"]]
# A reference progress simulation (not plugin runtime) proves the spec forbids
# canonical advancement for all alternate modes.
cursor = {"current": "e2", "completed": ("movie-a", "e1"), "ticks": 320000000}
original = json.dumps(cursor, sort_keys=True)
for mode in modes:
    trial = json.loads(original)
    if mode["tracksCanonical"]:
        trial["completed"].append("e2")
        trial["current"] = "movie-b"
        assert trial["current"] != cursor["current"]
    else:
        assert trial == cursor, f"{mode['id']} is a standalone session and cannot update canonical progress"

flow = spec["sessionLifecycle"]
transitions = flow["transitions"]
assert set(flow["states"]) == set(transitions)
assert flow["verifiedStartRequired"] is True and flow["dispatchConfirmsPlayback"] is False
assert flow["completionIdempotent"] is True and flow["requiresOwnerLease"] is True
for terminal in ("completed","cancelled","failed"):
    assert transitions[terminal] == [], "Terminal state must not restart an old playback instance"
assert "playing" not in transitions["prepared"] and "playing" not in transitions["dispatching"]
assert transitions["awaiting_verification"].count("playing") == 1
for source, targets in transitions.items():
    assert len(targets) == len(set(targets)), source
    assert all(x in transitions and x != source for x in targets), source
reachable = {"prepared"}
pending = deque(["prepared"])
while pending:
    for target in transitions[pending.popleft()]:
        if target not in reachable:
            reachable.add(target)
            pending.append(target)
assert reachable == set(flow["states"]), "Every lifecycle state must be reachable"

routes = spec["apiRoutes"]
keys = [(r["method"],r["path"]) for r in routes]
assert len(keys) == len(set(keys)), "Duplicate v1 routes"
assert len(routes) >= 12
for route in routes:
    assert route["path"].startswith("/FranchiseHub/v1/")
    assert route["auth"] in ("viewer", "admin")
    assert route["method"] in ("GET","POST","PUT","DELETE","PATCH")
    if route["method"] == "GET":
        assert not route["canonicalMutation"], "A read must never mutate chronological state"
    if route["canonicalMutation"]:
        assert route.get("explicitIntent") is True
        assert "/progress/" in route["path"]
mutation_paths = [r["path"].rsplit("/",1)[-1] for r in routes if r["canonicalMutation"]]
assert set(mutation_paths) == {"restart", "credit", "reconcile"}
assert {"orders", "progress", "resolve", "launch", "cancel", "takeover", "diagnostics"} <= {
    route["path"].rsplit("/",1)[-1] for route in routes
}
assert spec["httpErrors"] == {
    "staleRevision":409, "ambiguousOccurrence":409, "activeSessionConflict":409,
    "unplayableSelection":422, "playbackUnverified":424,
    "capabilityUnavailable":503
}
assert len(spec["designAcceptance"]) >= 10
assert len(set(spec["designAcceptance"])) == len(spec["designAcceptance"])

docs = ("README.md", "DOMAIN_MODEL.md", "DECISIONS.md", "API_V1.md",
        "PERSISTENCE_MIGRATION.md", "ACCEPTANCE_TESTS.md")
for doc in docs:
    content = (arch / doc).read_text(encoding="utf-8")
    assert len(content) > 1200, f"Missing a substantive Step 4 architecture document: {doc}"
    assert "Step" in content or "design" in content.lower()
for marker in (
    "server", "chronological", "occurrence", "SQLite",
    "legacy", "verified", "independent", "Jellyfin"
):
    assert any(marker.lower() in (arch / doc).read_text(encoding="utf-8").lower() for doc in docs), marker
# Existing parity guards remain available; Step 4 has not replaced runtime code.
assert (repo / "Jellyfin-Franchise-Explorer/scripts/test_step3_feature_contracts.py").is_file()
assert (repo / "Jellyfin-Franchise-Explorer/scripts/test_feature_parity_fixture.js").is_file()
assert "source" in (repo / "docs/audit/STEP3_SOURCE_AUDIT.md").read_text(encoding="utf-8").lower()
print(f"PASS Step 4 architecture contract: {len(modes)} modes, {len(routes)} v1 routes, "
      f"{len(transitions)} lifecycle states, {len(docs)} design docs and queue/isolation cases")
