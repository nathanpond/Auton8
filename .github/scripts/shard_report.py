#!/usr/bin/env python3
"""Read one shard's trx and emit its executed count plus a markdown summary.

Split out of ci.yml rather than inlined for two reasons. Inline Python inside a
`run: |` block has to stay indented under the block scalar or it silently ends
it, and building markdown in shell puts backticks inside double quotes, where
the shell substitutes them -- #74 flags exactly that hazard. A file is also
testable, which ShardReportScriptTests relies on.

The executed count is the number reconcile_shards.py checks. A shard that
crashed before writing a trx reports 0, which is deliberate: reconciliation
should see the loss rather than skip the shard.
"""

from __future__ import annotations

import argparse
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def read_trx(path: Path) -> dict:
    root = ET.parse(path).getroot()
    counters = root.find("t:ResultSummary/t:Counters", NS)
    if counters is None:
        raise ValueError(f"{path} has no ResultSummary/Counters element.")

    failures = []
    for result in root.iter("{%s}UnitTestResult" % NS["t"]):
        if result.get("outcome") != "Failed":
            continue
        message = (
            result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
            or ""
        ).strip()
        first = message.splitlines()[0] if message else ""
        failures.append((result.get("testName") or "?", first))

    return {
        "total": int(counters.get("total") or 0),
        "passed": int(counters.get("passed") or 0),
        "failed": int(counters.get("failed") or 0),
        "failures": failures,
    }


def markdown(shard: str, elapsed: int, data: dict | None) -> str:
    minutes, seconds = divmod(max(elapsed, 0), 60)
    lines = [f"### Shard {shard}", ""]

    if data is None:
        lines += [
            f"**No trx produced** after {minutes}m {seconds}s — the shard did not "
            "finish writing results. Reconciliation counts this as 0 executed.",
        ]
        return "\n".join(lines) + "\n"

    lines += [
        "| executed | passed | failed | elapsed |",
        "|---|---|---|---|",
        f"| {data['total']} | {data['passed']} | {data['failed']} | {minutes}m {seconds}s |",
    ]

    if data["failures"]:
        # A red shard has to be diagnosable without opening its log.
        lines += ["", "**Failures**", "", "| test | first assertion |", "|---|---|"]
        for name, message in data["failures"]:
            # Escape the pipe so a message containing one cannot break the table.
            safe = message.replace("|", "\\|")
            lines.append(f"| `{name}` | {safe} |")

    return "\n".join(lines) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--trx", required=True, help="Path to the shard's trx (may not exist).")
    ap.add_argument("--shard", required=True)
    ap.add_argument("--elapsed", type=int, default=0)
    ap.add_argument("--count-file", required=True)
    ap.add_argument("--summary-file", required=True)
    args = ap.parse_args()

    trx = Path(args.trx)
    data = read_trx(trx) if trx.is_file() else None
    executed = data["total"] if data else 0

    Path(args.count_file).write_text(f"shard={args.shard}\nexecuted={executed}\n")
    Path(args.summary_file).write_text(markdown(args.shard, args.elapsed, data))

    print(f"shard {args.shard}: executed {executed}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
