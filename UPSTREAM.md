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

## Status

This is a **frozen upstream baseline only**. The original plugin GUID, product identity, build/release references and Jellyfin injection identifiers are intentionally unchanged until **Step 2 — Independent Plugin Identity and Versioning**. Do not install the upstream plugin and this unmodified snapshot as separate active plugins on the same Jellyfin instance.

No independent production release has been created. A future development branch and release process will add the hardening work planned for this project.
