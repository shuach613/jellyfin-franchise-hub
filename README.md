# Franchise Hub for Jellyfin

**Independent development preview — not yet ready for installation. No release has been published.**

Franchise Hub is a personal, noncommercial derivative of [Gost1o1's Jellyfin Franchise Explorer](https://github.com/Gost1o1/Jellyfin-Franchise-Explorer), based on the exact upstream `0.8.9.28` snapshot [`e02be642c9a89e66cfd85f458e6fe0bbebd3714c`](https://github.com/Gost1o1/Jellyfin-Franchise-Explorer/commit/e02be642c9a89e66cfd85f458e6fe0bbebd3714c). This is a separate project, not endorsed or maintained by the original authors.

## Project identity (Step 2)

- Plugin name: **Franchise Hub**
- Jellyfin plugin GUID: `4f0ea498-68ea-42ce-a010-bd35b182299d`
- Assembly: `Jellyfin.Plugin.FranchiseHub.dll`
- Development version: `0.1.0.0`
- JavaScript Injector registration: `jellyfin-franchise-hub-my-media-v0100`
- Current Jellyfin target: **12.2** with **.NET 10**
- GitHub repo: [`shuach613/jellyfin-franchise-hub`](https://github.com/shuach613/jellyfin-franchise-hub)
- **Plugin catalog:** [`manifest.json`](manifest.json) — currently intentionally has no installable versions. It will receive only verified releases from this repository when the release pipeline is implemented.

## Development status

The runtime implementation is still the imported upstream behavior with Step 2 identity changes. Step 3 added a parity baseline, and Step 4 provides a **design-only** server-owned chronology architecture; new v1 endpoints, SQLite storage and selective sessions are not implemented yet. Existing API routes and internal namespaces remain temporarily unchanged to retain compatibility during the forthcoming refactor. **Chronological playback, automatic next-item playback and native-client behavior are not yet verified for production.**

Do **not** install this development snapshot alongside the original Franchise Explorer. Their existing routes and browser state integrations still overlap; a safe migration path will be implemented in later steps. No upstream user configuration is automatically migrated by changing the plugin GUID.

- [Upstream attribution and frozen-baseline verification](UPSTREAM.md)
- [Original upstream README (archived verbatim)](docs/UPSTREAM_README.md)
- [Planned roadmap](ROADMAP.md)
- [Step 3 — source audit and risk register](docs/audit/STEP3_SOURCE_AUDIT.md)
- [Step 3 — complete feature-parity matrix](docs/audit/FEATURE_PARITY_MATRIX.md)
- [Step 3 — regression tests, fixtures and validation gaps](docs/audit/TEST_PLAN.md)
- [Step 4 — unified franchise/playback architecture](docs/architecture/README.md)
- [Step 4 — domain model and occurrence identities](docs/architecture/DOMAIN_MODEL.md)
- [Step 4 — proposed versioned API](docs/architecture/API_V1.md)
- [Step 4 — persistence and migration design](docs/architecture/PERSISTENCE_MIGRATION.md)
- [Step 4 — architecture decisions and acceptance scenarios](docs/architecture/DECISIONS.md)

## Legal

The copied material retains the original **Personal and Noncommercial Use License (Draft)** and copyright notices. Read [LICENSE](LICENSE). Only personal, noncommercial use and permitted distribution are covered by this license; a separate license from the upstream copyright holder is needed for commercial uses. Changes are not affiliated with or endorsed by the upstream author.
