# Test fixtures — torrent release names

Golden-file inputs for `TorrentCandidateParser` and related Core tests (Sprint 1+).

**Source:** Release filenames from the developer's qBittorrent library (Aug 2026). Public anime/movie titles only — no local paths, magnets, or info hashes.

**Usage (Sprint 1):** Load `torrent-release-names.json` in xUnit `[MemberData]` or copy cases into `TorrentCandidateParserTests`. Expected fields are **documentation targets** — verify against actual parser output when writing tests; update JSON if parser behavior is intentionally changed.

**Decision:** Open question #3 in [02-unit-tests-critical-paths.md](../02-unit-tests-critical-paths.md) — **approved**, anonymized names committed.
