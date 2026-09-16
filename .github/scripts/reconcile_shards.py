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


def read_counts(root: Path) -> list[tuple[str, int, int]]:
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
        # Absent in count files written before #476. Zero is the right default
        # for those: an old artifact cannot report a skip it never looked for,
        # and treating the absence as a failure would fail the gate for a reason
        # that is not about the tests.
        try:
            skipped = int(fields.get("skipped", "0"))
        except ValueError:
            skipped = 0
        counts.append((shard, executed, skipped))
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
    total = sum(executed for _, executed, _ in counts)
    skipped = sum(skip for _, _, skip in counts)

    lines = [
        "### Reconciliation",
        "",
        "| shard | executed | skipped |",
        "|---|---|---|",
    ]
    lines += [f"| {shard} | {executed} | {skip} |" for shard, executed, skip in counts]
    lines += [
        f"| **sum** | **{total}** | **{skipped}** |",
        f"| **discovered** | **{args.expected}** | |",
    ]

    # A SKIP IS A LOST TEST (#476). A trx counts a skipped test in `total`, so
    # the sum above stays whole and reconciliation used to pass: one attribute
    # argument took a test out of the merge gate with every number matching.
    # Checked here rather than per shard because this job is `always()`, so a
    # skip cannot hide behind a failing shard -- the case where it matters most.
    if skipped:
        lines += [
            "",
            f"> **{skipped} test(s) skipped.** The slim tier runs everything it "
            "discovers or it fails.",
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

    if skipped:
        print(
            f"::error::{skipped} test(s) were skipped in the backend shards. A trx "
            "counts a skipped test in `total`, so this reconciles perfectly while the "
            "test does not run -- which is why it is checked separately (#476). Remove "
            "the Skip, or move the test out of the tier deliberately and update the pin.",
            file=sys.stderr,
        )
        return 1

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
