"""Effective search parameters per recipe, following the real code paths (read-only).

Reads <state>/Recipes/*.rcp, <state>/settings.json (AutoTorrent block only) and the SQLite DB
(read-only) to show which recipe goes through which search path and with which values.
Marks: no mark = from recipe, (s) = settings.json fallback, (c) = code default.

  python -I -X utf8 recipe_effective.py [--state D:\\MediaManagerState] [--markdown]

Mirror of MediaManager.Core/Services/RecipeRuntimeSettings.cs + FetchJobService /
ShowSearchSnapshotService / AutomationFlowService dispatch as of commit ff2caa3. Update when
those change. Never prints credentials.
"""
import argparse
import collections
import glob
import json
import os
import sqlite3
import sys


def default_state():
    pointer = os.path.join(os.environ.get("LOCALAPPDATA", ""), "MediaManager", "state-path.txt")
    if os.path.exists(pointer):
        value = open(pointer, encoding="utf-8-sig").read().strip()
        if value:
            return value
    return os.path.join(os.environ.get("LOCALAPPDATA", ""), "MediaManager", "State")


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def ext(m):
    return (m or {}).get("extensionData") or {}


def gi(m, key, fb, lo, hi, src):
    try:
        return f"{clamp(int(ext(m)[key]), lo, hi)}"
    except (KeyError, ValueError, TypeError):
        return f"{clamp(fb, lo, hi)}{src}"


def gb(m, key, fb):
    v = ext(m).get(key)
    if v is not None and str(v).lower() in ("true", "false"):
        return str(v).lower() == "true"
    return fb


def usage(state):
    use = collections.Counter()
    db = os.path.join(state, "media-manager.db")
    if not os.path.exists(db):
        return use
    c = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    for rid, n in c.execute("select RecipeId, count(*) from TrackedShows group by RecipeId"):
        use[(rid, "ep")] += n
    for rid, n in c.execute("select PackRecipeId, count(*) from TrackedShows group by PackRecipeId"):
        use[(rid, "pack")] += n
    for rid, n in c.execute("select RecipeId, count(*) from TrackedMovies group by RecipeId"):
        use[(rid, "movie")] += n
    for rid, n in c.execute("select RecipeId, count(*) from TrackedShows where AutoTrackAnchorDayOfWeek is not null group by RecipeId"):
        use[(rid, "autotrack")] += n
    return use


def effective(recipe, at):
    kind, mods = recipe["targetKind"], recipe["modules"]
    first = lambda bt: next((m for m in mods if m["blockType"] == bt), None)
    first_enabled = lambda bt: next((m for m in mods if m["blockType"] == bt and m.get("isEnabled", True)), None)
    ss, sse, qb = first("SearchSource"), first_enabled("SearchSource"), first_enabled("QueryBuilder")
    snap = gb(ss, "useShowSnapshotSearch", at.get("UseShowSnapshotSearch", False))
    pag = gb(ss, "enableSearchPagination", kind != "TvEpisode")
    limit = (sse or {}).get("resultLimit") or 100
    s, c = "(s)", "(c)"
    if kind == "TvSeasonPack" or (kind == "TvEpisode" and snap):
        path = "snapshot"
        timeout = gi(ss, "snapshotTimeoutSeconds", at.get("SnapshotTimeoutSeconds", 120), 30, 300, s)
        idle = gi(ss, "snapshotIdleTimeoutSeconds", at.get("SnapshotIdleTimeoutSeconds", 10), 0, 120, s)
        read = "offset0 limit " + gi(ss, "snapshotTargetResults", at.get("SnapshotTargetResults", 2000), 100, 5000, s)
        par = "1"
    elif kind == "TvEpisode":
        timeout = gi(ss, "parallelSearchTimeoutSeconds", at.get("ParallelSearchTimeoutSeconds", 30), 10, 300, s)
        par = gi(ss, "parallelSearchCount", at.get("MaxParallelSearches", 3), 1, 8, s) + " episodes"
        if pag:
            path = "per-episode pagination (FetchJobService)"
            idle = gi(ss, "paginationIdleTimeoutSecondsTvParallel", 6, 0, 120, c)
            read = f"first {gi(ss, 'paginationMaxPagesTvParallel', 1, 1, 10, c)}x{gi(ss, 'paginationPageSize', 500, 50, 1000, c)} rows"
        else:
            path = "per-episode client.SearchAsync"
            idle = gi(ss, "searchIdleTimeoutSecondsTvParallel", 8, 0, 120, c)
            read = f"offset0 limit {limit}"
    else:
        timeout = gi(ss, "movieSearchTimeoutSeconds", at.get("MovieSearchTimeoutSeconds", 30), 10, 300, s)
        par = gi(ss, "parallelSearchCount", at.get("MaxParallelSearches", 3), 1, 8, s) + " queries"
        if pag:
            path = "movie cursor pagination (AutomationFlowService)"
            idle = gi(ss, "paginationIdleTimeoutSecondsMovie", 10, 0, 120, c)
            read = f"cursor, cap {gi(ss, 'paginationMaxTotalResults', 1500, 100, 5000, c)}"
        else:
            path = "movie client.SearchAsync"
            idle = gi(ss, "searchIdleTimeoutSecondsMovie", 10, 0, 120, c)
            read = f"offset0 limit {limit}"
    idm = first("Identity")
    plugins = (sse or {}).get("plugins") or "enabled"
    return {
        "name": recipe["name"], "kind": kind, "path": path, "timeout": timeout, "idle": idle, "read": read,
        "parallel": par, "maxCandidates": gi(ss, "maxCandidatesPerFetch", at.get("MaxCandidatesPerFetch", 3), 1, 10, s),
        "plugins": plugins if plugins == "enabled" else f"{len(plugins.split('|'))} picked",
        "altTitles": gb(idm, "useLibraryEnglishTitles", False) and (idm or {}).get("isEnabled", True),
        "templates": (qb or {}).get("queryTemplates") or [],
        "templatesDroppedBySnapshot": path == "snapshot" and any(
            t for t in (qb or {}).get("queryTemplates") or [] if any(k in t for k in ("{season", "{episode"))),
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--state", default=default_state())
    ap.add_argument("--markdown", action="store_true")
    a = ap.parse_args()
    settings = json.load(open(os.path.join(a.state, "settings.json"), encoding="utf-8-sig"))
    at = settings.get("AutoTorrent") or {}
    use = usage(a.state)
    rows = []
    for f in sorted(glob.glob(os.path.join(a.state, "Recipes", "*.rcp"))):
        r = json.load(open(f, encoding="utf-8-sig"))
        e = effective(r, at)
        e["used"] = {k[1]: v for k, v in use.items() if k[0] == r["recipeId"]}
        rows.append(e)
    rows.sort(key=lambda e: -sum(e["used"].values()))
    if not a.markdown:
        for e in rows:
            print(json.dumps(e, ensure_ascii=False))
        return 0
    print("| Recipe | Kind | Used | Path | Timeout | Idle | Read | Parallel | Max cand. | Plugins | Alt titles | Templates dropped |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for e in rows:
        used = ", ".join(f"{k} {v}" for k, v in e["used"].items()) or "-"
        print(f"| {e['name']} | {e['kind']} | {used} | {e['path']} | {e['timeout']} | {e['idle']} | {e['read']} | "
              f"{e['parallel']} | {e['maxCandidates']} | {e['plugins']} | {'yes' if e['altTitles'] else 'no'} | "
              f"{'YES' if e['templatesDroppedBySnapshot'] else ''} |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
