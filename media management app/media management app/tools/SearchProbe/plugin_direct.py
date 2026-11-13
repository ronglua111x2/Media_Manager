"""Run installed qBittorrent search plugins directly (no qBittorrent) to see WHY an engine is empty.

qBittorrent swallows plugin exceptions, so an engine that crashes or gets blocked just looks
"empty". This runs <engine>().search(query) inside the nova3 folder, one subprocess per engine with
a timeout, and reports: rows printed, elapsed time, and the last error line (traceback / HTTP).

  python -X utf8 plugin_direct.py --engines animetosho,therarbg --query "Coherence" [--timeout 60]

Note: this executes the plugin code exactly like qBittorrent does (same files, same Python).
It must run WITHOUT -I, because the plugins import `helpers` / `novaprinter` from the nova3 folder.
Only search() is called, never download_torrent().
"""
import argparse
import os
import subprocess
import sys
import time

RUNNER = r"""
import sys, importlib, urllib.parse
sys.path.insert(0, sys.argv[1]); sys.path.insert(0, sys.argv[1] + '/engines')
mod = importlib.import_module('engines.' + sys.argv[2])
# nova2.py quotes the query before handing it to the plugin (nova2.py: urllib.parse.quote)
getattr(mod, sys.argv[2])().search(urllib.parse.quote(sys.argv[3]), 'all')
"""


def nova_dir():
    return os.path.join(os.environ.get("LOCALAPPDATA", ""), "qBittorrent", "nova3")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--engines", required=True)
    ap.add_argument("--query", required=True)
    ap.add_argument("--timeout", type=int, default=60)
    ap.add_argument("--nova", default=nova_dir())
    a = ap.parse_args()
    print(f"| Engine | Rows | Seconds | Result |\n|---|---|---|---|")
    for engine in [e.strip() for e in a.engines.split(",") if e.strip()]:
        start = time.monotonic()
        try:
            p = subprocess.run([sys.executable, "-X", "utf8", "-c", RUNNER, a.nova, engine, a.query],
                               capture_output=True, text=True, encoding="utf-8", errors="replace",
                               timeout=a.timeout, cwd=a.nova)
            rows = sum(1 for line in p.stdout.splitlines() if line.count("|") >= 5)
            err = [line.strip() for line in p.stderr.splitlines() if line.strip()]
            result = err[-1][:160] if err else ("ok" if rows else "no rows, no error")
        except subprocess.TimeoutExpired:
            rows, result = 0, f"TIMEOUT after {a.timeout}s (would keep the qBittorrent job Running)"
        print(f"| {engine} | {rows} | {time.monotonic() - start:.1f} | {result.replace('|', '/')} |", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
