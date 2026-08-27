# Legacy overlap — leftover settings vs current core

**Status:** Docs only. No schema drop, no matching change.  
**Trigger:** After qBittorrent API recovery, TV cart with a High Quality recipe kept only 1080p while a manual qBittorrent search for the same show title showed 2160p. Cause is leftover **per-show Auto Torrent** columns still used as the TV snapshot/pack quality gate.

This folder inventories leftover DB columns and code paths that **still own a job the current core (recipes / Auto-Track policy) is supposed to own**. It does **not** implement a fix.

## Hard rule

Do **not** `DROP` or migrate away `PreferredQuality`, `PreferredAudioCodec`, `MinimumSeeders`, or `FetchJobs` in the first code pass. Keep writing defaults so SQLite `NOT NULL` stays valid. A later code plan should **stop reading leftover columns in matching**.

## Documents (read in order)

| # | File | Contents |
|---|------|----------|
| 0 | [00-executive-summary.md](./00-executive-summary.md) | Why 4K TV fails and 4K movies work; leftover vs recipe vs Auto-Track |
| 1 | [01-column-inventory.md](./01-column-inventory.md) | Columns/APIs: writers, readers, UI, live values, classification |
| 2 | [02-conflict-map.md](./02-conflict-map.md) | Who wins per flow (cart TV, hunt, movie, pack) |
| 3 | [03-evaluation-paths.md](./03-evaluation-paths.md) | Matchers and cart-debug writers |
| 4 | [04-decouple-plan.md](./04-decouple-plan.md) | Sequenced later-code outline — no DROP |

## Related

- Live trigger notes: [debug/torrent-hunt/](../debug/torrent-hunt/)
- Recipe schema (current core): [RECIPE_SCHEMA.md](../RECIPE_SCHEMA.md)
- Schema baseline: [planning/schema-inventory.md](../planning/schema-inventory.md)
- State folder / FetchJobs purge: [STATE_FOLDER.md](../STATE_FOLDER.md)

## Out of scope (this folder)

- App code changes
- SQL migrations / column drops
- Settings 7-VM split ([settings-modernization](../settings-modernization/) is frozen)
