# Step 4 architecture acceptance scenarios

These are specification-level tests and future integration requirements. Passing the Step 4 design validator does not mean the new playback engine is implemented. Steps 5–15 must provide actual behavioral and real-Jellyfin tests.

## Golden synthetic chronology

Define the canonical franchise list as:
- slot m1 -> movie-a;
- slot s1 -> series-a/e1, series-a/e2;
- slot m2 -> movie-b;
- slot s2 -> series-a/e3.

The canonical resolved queue is [movie-a, e1, e2, movie-b, e3]. An on-screen row showing movies before shows **does not** alter this chronology. A native Series expansion has e4 too but e4 was intentionally omitted from this canonical order.

| Mode | Expected items | Canonical progress change |
| --- | --- | --- |
| chronological | movie-a, e1, e2, movie-b, e3 | Only after verified tracked events |
| movies_only | movie-a, movie-b | None |
| series_only | e1, e2, e3 | None |
| complete_series (series-a) | e1, e2, e3, e4 | None |
| selected_era | e1, e2, movie-b | None |
| custom_selection | movie-b, e2, movie-a | None |
| release_order (configured) | movie-b, movie-a, e1, e2, e3 | None |
| watch_only | e2 | None |

All filters preserve the canonical list's **relative order**, except custom and separately defined release order. Complete Series follows the selected full series episode policy. Empty selection means 422, not a silent fallback.

## Required domain-level scenarios

1. **Never override chronology:** a user maps a TV episode as an independent movie and manually interleaves a new movie between two seasons. The resolver follows the configured exact slots, not Jellyfin's Series Next Up.
2. **Reorder without reset:** move movie-b before e2; position/completion remain anchored to occurrence IDs.
3. **Duplicate physical movie:** place movie-a twice intentionally; completion of first occurrence must not mark second done.
4. **Episode bundle/split:** preserve verified unique episode history when bundled references become individual episode slots; ambiguous repeated episodes require review.
5. **Media rescan:** verified provider identity maps a replaced item ID without changing occurrence history; conflicting providers do not auto-map.
6. **Canonical resume:** browse, homepage and player resolve exactly the same next occurrence, ticks, and order revision.
7. **Independent session:** watch entire series-a (includes e4) then return to franchise; canonical cursor/completed occurrence set unchanged.
8. **Two devices:** second device must explicitly take over canonical session; old events cannot advance after revocation.
9. **Unverified command:** Jellyfin remote API returns 204 but does not start expected item -> playback_unverified and no change.
10. **Out-of-order callbacks:** stop-before-start, duplicate completions, stale position ticks and unrelated player sessions cannot corrupt progress.
11. **Near-end vs seeking:** valid natural-end evidence may complete once; seeks to end or pauses must not.
12. **Edit while playing:** session's immutable snapshot is kept; next occurrence reconciles on safe revision boundary, or requires user review.
13. **User permissions:** media disappears or becomes inaccessible before auto-next -> no launch of forbidden item, structured warning and recoverable pause.
14. **Original import/export:** old version's exported config imports into new version; new legacy-compatible export opens in unmodified original without losing representable rows, manually assigned item ranges or source exclusion metadata.
15. **Native backup:** assets + opt-in progress restore without leaking another user's playback history; rollback restores original XML/JSON and artwork.
16. **Web client compatibility:** normal Jellyfin Web desktop/mobile browser playback verified on actual Jellyfin 12.2, with Injector, reverse proxy, local handoff, and device limitations documented.

## State-machine contracts

prepared -> dispatching -> awaiting_verification -> playing -> advancing -> awaiting_verification -> ... -> completed.

paused may resume only after a fresh verified start, not via a browser-only status flip. failed and cancelled are terminal. A transition cannot emit duplicate completion or orphan playback claims.

## Exit gate for Step 4

- The architecture's roles, APIs, data schema, identity and concurrency guarantees are documented.
- Canonical vs alternative playback behavior is expressible in machine-readable sample contracts.
- CI validates action/mode coverage and transition/permission invariants.
- No runtime playback implementation or data migration is represented as done.
- Existing feature-parity tests and Jellyfin plugin compilation remain green.

Later steps must replace static/model checks with executable service and real-device tests before v1.0 release.
