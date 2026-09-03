#!/usr/bin/env python3
"""Fail the build if the shards did not run every discovered test.

This is the guard the whole sharded structure exists for. A `--filter`
expression that silently matches nothing -- a renamed namespace, an escaping
bug, a class name that is a prefix of another -- runs no tests and reports
success. The symptom is a *faster, greener* build, which is the one failure
nobody investigates.

Ordering matters here. A count mismatch is reported before any shard failure,
because "these tests never ran" and "these tests failed" call for completely
different responses, and the second must not hide the first.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


def read_counts(root: Path) -> list[tuple[str, int]]:
    counts = []
    for path in sorted(root.glob("**/shard-count.txt")):
        fields = {}
        for line in path.read_text().splitlines():
            if "=" in line:
                key, _, value = line.partition("=")
                fields[key.strip()] = value.strip()
        shard = fields.get("shard", "?")
        try:
            executed = int(fields.get("executed", "0"))
        except ValueError:
            executed = 0
        counts.append((shard, executed))
    return counts


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--counts-dir", required=True)
    ap.add_argument("--expected", type=int, required=True)
    ap.add_argument("--shards-result", default="success",
                    help="The matrix job's aggregate result.")
    ap.add_argument("--summary-file", default="")
    args = ap.parse_args()

    counts = read_counts(Path(args.counts_dir))
    total = sum(executed for _, executed in counts)

    lines = [
        "### Reconciliation",
        "",
        "| shard | executed |",
        "|---|---|",
    ]
    lines += [f"| {shard} | {executed} |" for shard, executed in counts]
    lines += [
        f"| **sum** | **{total}** |",
        f"| **discovered** | **{args.expected}** |",
    ]

    lost = total != args.expected
    if lost:
        lines += [
            "",
            f"> **Test loss detected.** The shards ran `{total}` of "
            f"`{args.expected}` discovered test cases.",
        ]

    if not counts:
        lines += ["", "> **No shard counts found at all.** Every shard failed to "
                  "publish one, so nothing can be reconciled."]

    if args.summary_file:
        Path(args.summary_file).open("a").write("\n".join(lines) + "\n")

    if lost or not counts:
        print(
            f"::error::Sharding lost tests: {total} executed, {args.expected} "
            "discovered. A shard's filter matched fewer tests than it should — "
            "this is the failure that otherwise shows up as a faster, greener build.",
            file=sys.stderr,
        )
        return 1

    print(f"All {args.expected} discovered test cases ran.")

    if args.shards_result != "success":
        print(
            f"::error::All {args.expected} tests ran, but at least one shard "
            f"reported failures (matrix result: {args.shards_result}). See each "
            "shard's summary.",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
