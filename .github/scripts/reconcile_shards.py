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


def read_counts(root: Path) -> tuple[list[tuple[str, int, int]], list[str]]:
    """Returns (counts, unreadable) -- the second list is NEVER folded into the first.

    #492: the previous version reported an unparseable `skipped=` as `-1` and let
    `main` sum it with the real counts, so one bad file CANCELLED one genuine
    skip. Measured against the true pre-fix commit, same input (one malformed
    file, one real skip): before rc=1, after rc=0. A hardening that turned red
    into green. "Unknown" is not a quantity and must not be arithmetic.
    """
    counts = []
    unreadable = []

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

        # ABSENT IS UNREADABLE TOO (#492). The previous comment here excused a
        # missing `skipped=` as "an old artifact", but these files are written
        # fresh in the same workflow run, from the same commit, with
        # retention-days: 1. There is no old artifact. A shard_report.py
        # regression that DROPS the line is exactly as dangerous as one that
        # garbles it, and the absence was the path that still failed open.
        raw = fields.get("skipped")
        try:
            if raw is None:
                raise ValueError("no `skipped=` line")
            skipped = int(raw)
        except ValueError as problem:
            unreadable.append(f"{path}: {problem} (skipped={raw!r})")
            continue

        counts.append((shard, executed, skipped))

    return counts, unreadable


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--counts-dir", required=True)
    ap.add_argument("--expected", type=int, required=True)
    ap.add_argument("--shards-result", default="success",
                    help="The matrix job's aggregate result.")
    ap.add_argument("--summary-file", default="")
    args = ap.parse_args()

    counts, unreadable = read_counts(Path(args.counts_dir))
    total = sum(executed for _, executed, _ in counts)
    skipped = sum(skip for _, _, skip in counts)

    lines = [
        "### Reconciliation",
        "",
        "| shard | executed | skipped |",
        "|---|---|---|",
    ]
    lines += [f"| {shard} | {executed} | {skip} |" for shard, executed, skip in counts]
    lines += [f"| _(unreadable)_ | | {problem} |" for problem in unreadable]
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

    # BEFORE the arithmetic, and never part of it (#492). An unreadable file
    # means the skip total is unknown, and an unknown number of skips is not
    # zero skips -- nor is it a negative one that can offset a real skip
    # somewhere else, which is what the previous sentinel allowed.
    if unreadable:
        for problem in unreadable:
            print(f"::error::{problem}", file=sys.stderr)
        print(
            f"::error::{len(unreadable)} shard count file(s) could not be read, so the skip "
            "total is unknown. These are written fresh by shard_report.py in this same run, "
            "so an unreadable one is a defect in the producer, not an old artifact.",
            file=sys.stderr,
        )
        return 1

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
