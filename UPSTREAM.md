# Upstream source provenance and frozen baseline

This repository is an independently maintained, personal, noncommercial derivative of [Gost1o1/Jellyfin-Franchise-Explorer](https://github.com/Gost1o1/Jellyfin-Franchise-Explorer).

## Step 1 — Imported baseline

- **Pinned upstream commit:** [e02be642c9a89e66cfd85f458e6fe0bbebd3714c](https://github.com/Gost1o1/Jellyfin-Franchise-Explorer/commit/e02be642c9a89e66cfd85f458e6fe0bbebd3714c), from 2026-11-10 (plugin 0.8.9.28).
- **Verification:** All **239 of 239 upstream files** were copied in their original paths and matched their original Git blob hashes in the target repository; no missing or different upstream files.
- **Additional files:** `UPSTREAM.md` and `.github/workflows/restore-baseline-assets.yml` are independent bootstrap/provenance additions, not upstream files.
- **One-time import utility:** The workflow restored the large upstream documentation screenshots directly on a GitHub Actions runner after the GitHub Contents API was unable to transfer them.
- **History:** This is a file-exact snapshot, **not an original commit-history-preserving fork**. Original authorship and change history remain available in the linked upstream commit and repository.
- **Development:** No pull requests will be submitted upstream. Subsequent changes belong exclusively to this independent repository.

## License and attribution

The upstream project is licensed under the custom **Personal and Noncommercial Use License (Draft)**, version 0.1. The copied source retains its complete original LICENSE files and notices. All derivative changes must respect the permission, noncommercial restriction, preservation/attribution and modification-notice requirements. This repository is not endorsed by the upstream authors.

## Step 2 — independent identity

Franchise Hub now has a distinct Jellyfin plugin GUID, DLL/assembly name, injector script registration, unpublished catalog and version `0.1.0.0`. The original upstream snapshot remains frozen on `baseline/upstream-v0.8.9.28`. The original namespace and /FranchiseExplorer routes remain unchanged until the later compatibility and migration steps; this development build must not run simultaneously with upstream.

## Status

Step 1 is preserved as the original file-exact baseline on `baseline/upstream-v0.8.9.28`. Step 2 introduces the separate Franchise Hub identity and development metadata on `develop`. The new GUID is not a data migration: existing upstream configuration and user progress are **not** automatically copied, and source API routes/local browser state still overlap.

**Do not install the original plugin and this development build together.** Installation migration, full regression coverage and a production release are later roadmap steps. The Jellyfin catalog intentionally contains no published versions, and the independent release workflow fails closed until Step 16.
