# Franchise Hub — production readiness roadmap

1. [x] Establish the independent GitHub repository and exact upstream baseline.
2. [x] Establish independent plugin identity, versioning, script registration and an unpublished catalog.
3. [ ] Audit the original plugin and establish feature-parity tests.
4. [ ] Design the unified franchise and playback architecture.
5. [ ] Build the authoritative chronological watch-order engine.
6. [ ] Implement reliable data persistence and migration.
7. [ ] Implement bidirectional import/export compatibility with the original Explorer.
8. [ ] Develop verified playback sessions and progress tracking.
9. [ ] Rebuild and harden Jellyfin Web playback integration.
10. [ ] Implement chronological, movie-only, series-only, and custom playback modes.
11. [ ] Redesign the franchise browser and playback interface.
12. [ ] Rebuild the homepage, Continue Watching, and customizable library tile.
13. [ ] Integrate navigation and polish the complete interface.
14. [ ] Preserve and harden all existing features, security, and compatibility.
15. [ ] Conduct comprehensive automated and real-world Jellyfin testing.
16. [ ] Establish GitHub Actions, plugin packaging, and independent release management.
17. [ ] Validate installation, upgrades, backward compatibility, and rollback.
18. [ ] Publish the production-ready v1.0 release.

**Playback policy:** Franchise playback defaults to the saved chronological sequence. Filtered movies, whole-series, and other custom sessions must not silently modify chronological progress. **Compatibility policy:** The new plugin must import original Explorer exports and produce an export that the original importer can read, reporting unsupported new fields rather than discarding them silently.
