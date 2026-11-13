"""Summarize engine_probe.py output (jobs.jsonl) into Markdown tables.

  python -I -X utf8 analyze_engine_probe.py <out_dir> [--report report.md]

Per engine: finished/hung/error counts, median end time, rows per title.
Per title: which engines returned rows, plus extra checks for the special titles
(years seen in release names, apostrophe variants, accents).
"""
import argparse
import json
import os
import re
import statistics
import sys
from collections import Counter, defaultdict

YEAR = re.compile(r"(?<!\d)(19[2-9]\d|20[0-4]\d)(?!\d)")


def load(out_dir):
    with open(os.path.join(out_dir, "jobs.jsonl"), encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def classify(job):
    if job["error"]:
        return "error"
    if job["status"] == "Running":
        return "hung"  # still running at max-seconds
    return "ok" if job["rows"] else "empty"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir")
    ap.add_argument("--report", default="")
    a = ap.parse_args()
    jobs = load(a.out_dir)
    titles = list(dict.fromkeys(j["qid"] for j in jobs))
    engines = sorted({j["engine"] for j in jobs})
    by = {(j["engine"], j["qid"]): j for j in jobs}
    lines = []

    lines.append("## Engine health\n")
    lines.append("| Engine | ok | empty | hung | error | median end (s) | max end (s) | total rows |")
    lines.append("|---|---|---|---|---|---|---|---|")
    for e in engines:
        js = [j for j in jobs if j["engine"] == e]
        c = Counter(classify(j) for j in js)
        ends = [j["t_end"] for j in js if j["t_end"] is not None]
        lines.append(f"| {e} | {c['ok']} | {c['empty']} | {c['hung']} | {c['error']} | "
                     f"{statistics.median(ends) if ends else '-'} | {max(ends) if ends else '-'} | {sum(j['rows'] for j in js)} |")

    lines.append("\n## Rows per engine x title (h = still running at cutoff, E = error)\n")
    lines.append("| Engine | " + " | ".join(titles) + " |")
    lines.append("|---|" + "---|" * len(titles))
    for e in engines:
        cells = []
        for t in titles:
            j = by.get((e, t))
            if not j:
                cells.append("")
            elif j["error"]:
                cells.append("E")
            else:
                cells.append(f"{j['rows']}{'h' if j['status'] == 'Running' else ''}")
        lines.append(f"| {e} | " + " | ".join(cells) + " |")

    lines.append("\n## Per title\n")
    for t in titles:
        js = [j for j in jobs if j["qid"] == t]
        rows = [r for j in js for r in j["results"]]
        uniq = {r["url"] for r in rows}
        years = Counter(y for r in rows for y in YEAR.findall(r["name"] or ""))
        lines.append(f"- **{t}** `{js[0]['query']}`: {len(rows)} rows, {len(uniq)} unique URLs, "
                     f"engines with rows: {sum(1 for j in js if j['rows'])}/{len(js)}; "
                     f"top years in names: {', '.join(f'{y}x{n}' for y, n in years.most_common(4)) or '-'}")

    text = "\n".join(lines)
    print(text)
    if a.report:
        with open(a.report, "w", encoding="utf-8") as f:
            f.write(text + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
