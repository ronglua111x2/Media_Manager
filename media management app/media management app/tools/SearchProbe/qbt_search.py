"""Shared helpers for the SearchProbe tools: a read-only qBittorrent search client.

Only search/start, search/results, search/stop, search/delete and search/plugins are used.
Nothing here can add a torrent.

Notes learned while auditing (docs/code-review/2026-11/07b-hunt-search.md):
- Search jobs belong to the WebUI *session*. Without a session cookie every request is a new
  session and the job id returns 404 right after search/start. With "bypass authentication for
  localhost" enabled, any first request already returns a session cookie, so no password is needed;
  we keep it in a cookie jar.
- In Vietnam trackers are blocked without Cloudflare WARP, so the tools refuse to run unless WARP
  reports Connected (pass --no-warp-check to skip on other networks).
"""
from __future__ import annotations

import http.cookiejar
import json
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter
from dataclasses import dataclass, field

DEFAULT_BASE = "http://localhost:8080/api/v2/"
DEFAULT_WARP = r"C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe"


class QbtSearch:
    def __init__(self, base: str = DEFAULT_BASE):
        self.base = base if base.endswith("/") else base + "/"
        self._opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.call("app/version")  # establishes the session cookie (localhost bypass)

    def call(self, path: str, data: dict | None = None, timeout: int = 20) -> str:
        body = urllib.parse.urlencode(data).encode() if data is not None else None
        with self._opener.open(urllib.request.Request(self.base + path, data=body), timeout=timeout) as r:
            return r.read().decode("utf-8", "replace")

    def plugins(self) -> list[dict]:
        return json.loads(self.call("search/plugins"))

    def start(self, query: str, plugins: str, category: str = "all", retries: int = 6) -> int:
        for attempt in range(retries):
            try:
                return json.loads(self.call("search/start", {"pattern": query, "plugins": plugins, "category": category}))["id"]
            except urllib.error.HTTPError as ex:
                if ex.code == 409 and attempt < retries - 1:  # too many concurrent search jobs
                    time.sleep(3)
                    continue
                raise
        raise RuntimeError("unreachable")

    def results(self, sid: int, offset: int, limit: int = 1000) -> dict:
        return json.loads(self.call(f"search/results?id={sid}&limit={limit}&offset={offset}"))

    def stop_and_delete(self, sid: int, running: bool) -> None:
        try:
            if running:
                self.call("search/stop", {"id": sid})
        finally:
            self.call("search/delete", {"id": sid})


@dataclass
class JobOutcome:
    status: str = "Unknown"
    rows: list[dict] = field(default_factory=list)
    t_first: float | None = None
    t_last: float | None = None
    t_end: float | None = None
    timeline: list[dict] = field(default_factory=list)
    error: str | None = None


def run_job(client: QbtSearch, query: str, plugins: str, max_seconds: int, keep_timeline: bool = False) -> JobOutcome:
    """Run one search job until it stops by itself (or max_seconds), reading the buffer with a cursor."""
    out = JobOutcome()
    sid = None
    start = time.monotonic()
    try:
        sid = client.start(query, plugins)
        start = time.monotonic()
        while True:
            t = round(time.monotonic() - start, 1)
            res = client.results(sid, offset=len(out.rows))
            new = res.get("results") or []
            if new:
                out.t_first = out.t_first if out.t_first is not None else t
                out.t_last = t
                out.rows.extend(new)
            out.status = res.get("status", "Unknown")
            total = res.get("total", len(out.rows))
            if keep_timeline:
                out.timeline.append({"t": t, "status": out.status, "total": total, "read": len(out.rows),
                                     "engines": dict(Counter(r.get("engineName") or "?" for r in out.rows))})
            if out.status != "Running" and len(out.rows) >= total:
                break
            if t >= max_seconds:
                break
            time.sleep(1)
        out.t_end = round(time.monotonic() - start, 1)
    except Exception as ex:  # report, never raise: one broken engine must not stop a probe run
        out.error = f"{type(ex).__name__}: {ex}"
    finally:
        if sid is not None:
            try:
                client.stop_and_delete(sid, running=out.status == "Running")
            except Exception:
                pass
    return out


def slim_rows(rows: list[dict]) -> list[dict]:
    return [{"name": r.get("fileName"), "engine": r.get("engineName"), "size": r.get("fileSize"),
             "seeds": r.get("nbSeeders"), "url": r.get("fileUrl") or r.get("descrLink"), "pub": r.get("pubDate")}
            for r in rows]


def warp_connected(warp_cli: str = DEFAULT_WARP) -> bool:
    try:
        out = subprocess.run([warp_cli, "status"], capture_output=True, text=True, timeout=15).stdout
        return "Connected" in out and "Disconnected" not in out
    except Exception:
        return False
