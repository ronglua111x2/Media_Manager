"""Engine health + special-title probe (read-only).

Runs ONE qBittorrent search job per (enabled plugin x title) so every engine is measured alone:
finished or hung, time to first/last row, rows, errors. Writes <out>/jobs.jsonl and <out>/plan.json.

  python -I -X utf8 engine_probe.py --out <dir> [--titles special_titles.json] [--engines a,b]
                                    [--workers 4] [--max-seconds 120] [--no-warp-check]

Then summarize with analyze_engine_probe.py.
"""
import argparse
import json
import os
import sys
import threading
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from qbt_search import DEFAULT_BASE, QbtSearch, run_job, slim_rows, warp_connected  # noqa: E402

_lock = threading.Lock()


def main() -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--titles", default=os.path.join(here, "special_titles.json"))
    ap.add_argument("--engines", default="", help="comma list; default = all enabled plugins")
    ap.add_argument("--workers", type=int, default=4, help="parallel jobs (qBittorrent caps concurrent searches)")
    ap.add_argument("--max-seconds", type=int, default=120)
    ap.add_argument("--base", default=DEFAULT_BASE)
    ap.add_argument("--no-warp-check", action="store_true")
    a = ap.parse_args()

    if not a.no_warp_check and not warp_connected():
        print("ABORT: Cloudflare WARP is not connected.", flush=True)
        return 2
    os.makedirs(a.out, exist_ok=True)
    titles = json.load(open(a.titles, encoding="utf-8"))
    client = QbtSearch(a.base)
    engines = [e.strip() for e in a.engines.split(",") if e.strip()] or sorted(
        p["name"] for p in client.plugins() if p["enabled"])
    json.dump({"engines": engines, "titles": titles, "max_seconds": a.max_seconds},
              open(os.path.join(a.out, "plan.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"engines={len(engines)} titles={len(titles)} jobs={len(engines) * len(titles)} workers={a.workers}", flush=True)

    def job(engine: str, title: dict) -> None:
        o = run_job(client, title["query"], engine, a.max_seconds)
        rec = {"engine": engine, "qid": title["id"], "query": title["query"], "status": o.status,
               "rows": len(o.rows), "t_first": o.t_first, "t_last": o.t_last, "t_end": o.t_end,
               "error": o.error, "results": slim_rows(o.rows)}
        with _lock:
            with open(os.path.join(a.out, "jobs.jsonl"), "a", encoding="utf-8") as f:
                f.write(json.dumps(rec, ensure_ascii=False) + "\n")
            print(f"{engine:16} {title['id']:15} status={o.status:8} rows={len(o.rows):4} end={o.t_end} err={o.error}", flush=True)

    # title-major order: the same site is not hit by many parallel jobs at once
    with ThreadPoolExecutor(max_workers=a.workers) as pool:
        for t in titles:
            for e in engines:
                pool.submit(job, e, t)
    print("DONE", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
