# Governor gaps audit (07d) — hand-over (2026-11-13)

Report (uncommitted): `docs/code-review/2026-11/07d-governor-gaps.md` in the native-search worktree. No code changed.

Key numbers (from shows_full.err of the 13/11 bench, 164 native calls):
- bitsearch + torrentdownloads hit 200/200 at call 107 (77 pack + 30 episode calls, target #29 "Show G S01 pack", show #15): ~1.87 req/call, ~10 req per pack, ~3.7 per episode (no early stop in bench).
- Pack forms (non-EZTV rows): "season N" 3811 rows/35 calls; "Sxx complete" / "Sxx pack" / "YEAR season N" give ~0 outside torrentdownloads (any-word RSS). Apostrophe-split variants only return rows with "season N".
- EZTV 0 ms (cache hit) in 143/164 calls.

Main findings:
- GOV-04 (pre-existing on main): Auto-Track re-hunts NoCandidates episodes every TMDB cycle (30 min; TorrentHuntIntervalMinutes unused), queue alphabetical Take(3) -> starvation + ~90 req/day per stuck episode on bitsearch/TD (background budget 140).
- GOV-03: no budget-aware trimming; recommend E (backoff) -> A (collapse pack forms) -> B (per-search cap + low-budget mode via AsyncLocal SearchSession).
- GOV-05: keep LimeTorrents 10/min 3 s, lower daily 500 -> 400; keep bitsearch/TD conservative.
- GOV-06/07: state JSON not flushed, fail-open on corrupt file, concurrent saves; SearchBench has its own governor (2x budget on same IP).
- GOV-01/02: jitter and EZTV long TTL: doc-only changes.
