"""Summarize hunt search lines from the app's systemlog files (read-only).

Extracts every finished search (snapshot, client.SearchAsync, FetchJob/AutomationFlow pagination)
with status at stop, why it stopped, rows and per-engine counts, plus engines that were empty in
every search.

  python -I -X utf8 hunt_log_summary.py [--logs D:\\MediaManagerState\\logs] [--since 20261101]
"""
import argparse
import glob
import os
import re
import sys
from collections import Counter

LINE = re.compile(r"^\[(?P<ts>[\d\- :.]+)\] \[\w+\] \[(?P<src>[\w.]+)\] \[\w+\] (?P<msg>.*)$")
PATTERNS = [
    ("snapshot", re.compile(r"Snapshot search completed\. Query='(?P<q>.*?)', Status='(?P<st>\w+)', Results=(?P<n>\d+), EndedBy='(?P<end>[^']*)', engines=\[(?P<eng>[^\]]*)\]")),
    ("client", re.compile(r"qBittorrent search completed\. Query='(?P<q>.*?)', Status='(?P<st>\w+)', Results=(?P<n>\d+).*?EndedBy='(?P<end>[^']*)', engines=\[(?P<eng>[^\]]*)\]")),
    ("fetch-pagination", re.compile(r"Fetch query pagination query='(?P<q>.*?)' .*?mergedRows=(?P<n>\d+) status='(?P<st>\w+)'.*?endedBy='(?P<end>[^']*)' engines=\[(?P<eng>[^\]]*)\]")),
    ("recipe-pagination", re.compile(r"Recipe search pagination query='(?P<q>.*?)' pages=.*?mergedRows=(?P<n>\d+) bufferTotal=(?P<buf>\d+).*?status='(?P<st>\w+)'.*?endedBy='(?P<end>[^']*)' engines=\[(?P<eng>[^\]]*)\]")),
]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--logs", default=r"D:\MediaManagerState\logs")
    ap.add_argument("--since", default="", help="yyyymmdd prefix filter on log file names")
    a = ap.parse_args()
    searches = []
    for path in sorted(glob.glob(os.path.join(a.logs, "*_systemlog*.txt"))):
        if a.since and os.path.basename(path)[:8] < a.since:
            continue
        for raw in open(path, encoding="utf-8", errors="replace"):
            m = LINE.match(raw.rstrip("\n"))
            if not m:
                continue
            for kind, rx in PATTERNS:
                p = rx.search(m["msg"])
                if p:
                    eng = dict(kv.rsplit(":", 1) for kv in p["eng"].split(", ") if ":" in kv)
                    searches.append({"ts": m["ts"], "kind": kind, "query": p["q"], "status": p["st"],
                                     "rows": int(p["n"]), "ended": p["end"], "engines": {k: int(v) for k, v in eng.items()}})
                    break
    if not searches:
        print("No hunt search lines found.")
        return 0
    print("| Time | Path | Query | Status at stop | Ended by | Rows | Empty engines |")
    print("|---|---|---|---|---|---|---|")
    for s in searches:
        empty = sorted(k for k, v in s["engines"].items() if v == 0)
        print(f"| {s['ts'][:16]} | {s['kind']} | {s['query']} | {s['status']} | {s['ended']} | {s['rows']} | {', '.join(empty)} |")
    still_running = sum(1 for s in searches if s["status"] == "Running")
    print(f"\nSearches: {len(searches)}; stopped while qBittorrent still Running: {still_running}")
    seen, empty_count = Counter(), Counter()
    for s in searches:
        for k, v in s["engines"].items():
            seen[k] += 1
            empty_count[k] += v == 0
    always_empty = sorted(k for k in seen if empty_count[k] == seen[k])
    print(f"Engines empty in every search they ran: {', '.join(always_empty) or '-'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
