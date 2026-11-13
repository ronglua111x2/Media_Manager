# native-search — batch 3 hand-over (2026-11-13 ~10:50)

Worktree `<repo>\.claude\worktrees\native-search`, branch `feat/native-search`, not pushed. Working tree clean at `6e7b77c`.

Commits (oldest first): f973281 pack alt titles + "S3 - 13" episode + repeated words; 42746f6 movie CamelCase group prefix/suffix + same-year prefix check for 1-2 word titles; aea8248 SearchSession (per-search cap, low budget, trimmed, req/hit telemetry, backend pinned, "No search source available", EZTV 2 h bg, indexer-state.json .bak/flush/cautious/lock, bench BenchState); 5a1cbe9 "Season 3 - 04", "[Title][17]", "第02集" are episodes; 917203c kickass detail-page magnets (≤3/search), torrents-csv `after` paging, apibay cat=200 + specific queries; 1657e5f comma sizes, WEB-DL group, TMDB per-request headers, cart picker filter; 6e7b77c docs (07c §6.4, 07d §6, doc 03, bench README).

Tests: Core 679, App 156 pass.

Evidence:
- `SearchBench reeval-shows` (new, offline). Old dumps lacked a show column; a script attached it from the .md order: scratch `<scratchpad>\shows_all.tsv` (13 083 rows). Accepted 906 -> 989, all new accepts correct.
- reeval-movies dump9: +16 correct, -4 wrong (Taxi Whats Movie Two x2, Burn Movie Two, bilibili two-film bundle); The Real Movie Three stays rejected.
- Bench (state = <SandboxFolder>\state, so the sandbox indexer-state.json now has bench usage): packs 4 shows (Show AB 6 good, Show O 7, Show M 1, Show I 4); movies 3 (Movie 01 21, Movie Two 76, Movie Three 100). No "trimmed" in interactive runs.
- Probes (6 governed requests): torrents-csv returns 10 rows/request, `after=<next>` honoured; apibay cat=200 keeps only video categories; LimeTorrents /movies/ = /all/ on page 1 (kept /all/).

Not done / open: P6 gate (user). UI not checked in the real app: cart picker filter fix and "No search source available" order status; the new build is published to `<SandboxFolder>\app_next` (Run sandbox.cmd swaps it in). LimeTorrents per-kind categories deliberately not used.

Note: SearchBench now writes `<state>/indexer-state.json` and refuses to run while the app holds `indexer-state.lock` (use `--force 1` or the sandbox state).
