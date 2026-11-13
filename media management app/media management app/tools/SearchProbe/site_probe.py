"""Probe candidate public torrent sites for a future native indexer (read-only, plain HTTP GET).

For each candidate search URL: HTTP status, time, content type, whether a bot challenge page came
back (Cloudflare "Just a moment", "One moment", captcha...), and rough result signals
(magnet links, info-hashes, RSS items, JSON). Does NOT try to solve or bypass challenges.

  python -I -X utf8 site_probe.py [--sites site_candidates.json] [--only name1,name2] [--no-warp-check]

Output: Markdown table on stdout.
"""
import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from qbt_search import warp_connected  # noqa: E402

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36"
CHALLENGE = re.compile(r"Just a moment|cf-chl|challenge-platform|One moment|captcha|Making sure you|DDoS-Guard|Checking your browser", re.I)
MAGNET = re.compile(r"magnet:\?xt=urn:btih:", re.I)
HASH = re.compile(r"(?<![0-9a-f])[0-9a-f]{40}(?![0-9a-f])", re.I)


def fetch(url, timeout):
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "*/*"})
    start = time.monotonic()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            body = r.read(3_000_000).decode("utf-8", "replace")
            return r.status, r.headers.get("Content-Type", ""), body, time.monotonic() - start, r.geturl(), None
    except urllib.error.HTTPError as ex:
        body = ex.read(200_000).decode("utf-8", "replace") if ex.fp else ""
        return ex.code, ex.headers.get("Content-Type", "") if ex.headers else "", body, time.monotonic() - start, url, None
    except Exception as ex:
        return None, "", "", time.monotonic() - start, url, f"{type(ex).__name__}: {str(ex)[:90]}"


def main() -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--sites", default=os.path.join(here, "site_candidates.json"))
    ap.add_argument("--only", default="")
    ap.add_argument("--timeout", type=int, default=20)
    ap.add_argument("--no-warp-check", action="store_true")
    a = ap.parse_args()
    if not a.no_warp_check and not warp_connected():
        print("ABORT: Cloudflare WARP is not connected.")
        return 2
    only = {x.strip() for x in a.only.split(",") if x.strip()}
    print("| Site | Kind | Query | HTTP | Time | Type | Challenge | Magnets | Hashes | RSS items | Final URL / error |")
    print("|---|---|---|---|---|---|---|---|---|---|---|")
    for s in json.load(open(a.sites, encoding="utf-8")):
        if only and s["name"] not in only:
            continue
        url = s["url"].replace("{q}", urllib.parse.quote(s["query"]))
        st, ct, body, dt, final, err = fetch(url, a.timeout)
        chall = "YES" if body and CHALLENGE.search(body[:20000]) else ""
        kind = "json" if "json" in ct else "rss" if ("xml" in ct or "<rss" in body[:500]) else "html" if body else "-"
        print(f"| {s['name']} | {s.get('kind', '')} | {s['query']} | {st or '-'} | {dt:.1f}s | {kind} | {chall} | "
              f"{len(MAGNET.findall(body))} | {len(set(HASH.findall(body)))} | {body.count('<item')} | "
              f"{(err or (final if final != url else '')).replace('|', '/')[:70]} |", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
