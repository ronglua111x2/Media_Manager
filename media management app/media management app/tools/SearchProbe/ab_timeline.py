"""A/B timeline probe: how many rows would the app have kept vs waiting for the job to finish.

For each case runs ONE job (with the plugin set the recipe uses), records a per-second timeline
(total rows, rows per engine) until the job stops by itself or --max-seconds, then simulates the
app's stop rule: stop at `timeout` or after `idle` seconds without new rows.

  python -I -X utf8 ab_timeline.py --out <dir> [--cases ab_cases.json] [--only id1,id2] [--no-warp-check]

Case fields: id, query, plugins ("enabled" or "a|b|c"), timeout, idle, note.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from qbt_search import DEFAULT_BASE, QbtSearch, run_job, slim_rows, warp_connected  # noqa: E402


def simulate_app_stop(timeline, timeout, idle):
    """Return (t_stop, rows_read, reason) for the app's timeout/idle rule over a timeline."""
    last_growth_t, last_read = 0.0, 0
    for p in timeline:
        if p["read"] > last_read:
            last_read, last_growth_t = p["read"], p["t"]
        if p["status"] != "Running":
            return p["t"], p["read"], "status-finished"
        if idle and p["t"] - last_growth_t >= idle:
            return p["t"], p["read"], "idle-timeout"
        if p["t"] >= timeout:
            return p["t"], p["read"], "timeout"
    return (timeline[-1]["t"], timeline[-1]["read"], "probe-cutoff") if timeline else (0, 0, "no-data")


def main() -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--cases", default=os.path.join(here, "ab_cases.json"))
    ap.add_argument("--only", default="")
    ap.add_argument("--max-seconds", type=int, default=300)
    ap.add_argument("--base", default=DEFAULT_BASE)
    ap.add_argument("--no-warp-check", action="store_true")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    only = {x.strip() for x in a.only.split(",") if x.strip()}
    client = QbtSearch(a.base)
    for case in json.load(open(a.cases, encoding="utf-8")):
        if only and case["id"] not in only:
            continue
        if not a.no_warp_check and not warp_connected():
            print("ABORT: Cloudflare WARP is not connected.", flush=True)
            return 2
        o = run_job(client, case["query"], case["plugins"], a.max_seconds, keep_timeline=True)
        t_stop, kept, reason = simulate_app_stop(o.timeline, case.get("timeout", 60), case.get("idle", 0))
        summary = {**case, "final_status": o.status, "final_rows": len(o.rows), "seconds": o.t_end,
                   "app_stop_t": t_stop, "app_rows": kept, "app_reason": reason, "error": o.error}
        json.dump({**summary, "timeline": o.timeline, "results": slim_rows(o.rows)},
                  open(os.path.join(a.out, case["id"] + ".json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"{case['id']}: final={o.status} rows={len(o.rows)} in {o.t_end}s | app would keep {kept} "
              f"({reason} at {t_stop}s) err={o.error}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
