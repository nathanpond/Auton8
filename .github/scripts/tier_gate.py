#!/usr/bin/env python3
"""Assert a tier ran exactly the tests it is pinned at, and skipped none (#476).

The e2e job used to be a bare `dotnet test --filter ... --logger console`, with
no count floor and no trx to read. Two escapes, both silent:

  * add `[Trait("RequiresService","Flowable")]` to a slim class and the filter
    stops matching it. The job runs fewer tests and stays green -- the tier
    story's own wording advertised this as a feature.
  * `[Fact(Skip = "...")]` anywhere, and the test is counted in the trx's
    `total` while not running at all.

So: an EXACT pin, not a floor. Growth has to be as visible as loss, or the
number drifts upward and stops meaning anything -- the same reasoning as
`ExecutionOracleSizeTests`' 29 and the coverage ratchet.
"""
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--trx", required=True)
    ap.add_argument("--expected", type=int, required=True)
    ap.add_argument("--label", default="tier")
    ap.add_argument("--summary-file", default="")
    args = ap.parse_args()

    path = Path(args.trx)

    # A run that produced no trx did not run tests, whatever it exited with.
    # Same reasoning as the shard step: `dotnet test --no-build` has exited 0 in
    # total silence against an incomplete build output.
    if not path.is_file():
        print(f"::error::{args.label}: no trx at {path}. No tests ran.", file=sys.stderr)
        return 1

    counters = ET.parse(path).getroot().find("t:ResultSummary/t:Counters", NS)
    if counters is None:
        print(f"::error::{args.label}: {path} has no ResultSummary/Counters.", file=sys.stderr)
        return 1

    total = int(counters.get("total") or 0)
    passed = int(counters.get("passed") or 0)
    failed = int(counters.get("failed") or 0)
    executed = int(counters.get("executed") or 0)

    # NOT `notExecuted` alone (#485). VSTest does not populate it -- measured on
    # this repo's exact versions (xunit 2.9.0 / runner.visualstudio 2.8.2 /
    # Test.Sdk 17.10.0), a real `[Fact(Skip)]` produces
    #   <Counters total="3" executed="2" ... notExecuted="0" ... />
    # while the console prints `Skipped: 1`. The skip is the total-executed gap.
    #
    # The gap is also the better question: it counts everything that did not
    # run, whatever the runner chose to call it, so an aborted or not-runnable
    # test is caught too. `max` keeps this correct if a future SDK starts
    # filling the attribute in.
    skipped = max(int(counters.get("notExecuted") or 0), total - executed)

    lines = [
        f"### {args.label}",
        "",
        "| ran | passed | failed | skipped | pinned |",
        "|---|---|---|---|---|",
        f"| {total} | {passed} | {failed} | {skipped} | {args.expected} |",
    ]

    problems = []

    if total != args.expected:
        direction = "left" if total < args.expected else "joined"
        problems.append(
            f"{args.label} ran {total} tests, pinned at {args.expected}. Tests {direction} "
            "this tier. If that was deliberate -- a test deleted, renamed, or given a "
            "RequiresService trait -- update the pin in tests/tiers.env in the SAME commit, "
            "so the change is a diff a reviewer sees rather than a number that absorbed it."
        )

    if skipped:
        problems.append(
            f"{args.label} skipped {skipped} test(s). A trx counts a skipped test in `total`, "
            "so the count above can match while the test does not run. The tier runs "
            "everything it discovers or it fails."
        )

    if args.summary_file:
        if problems:
            lines += [""] + [f"> **{p}**" for p in problems]
        Path(args.summary_file).open("a").write("\n".join(lines) + "\n")

    for problem in problems:
        print(f"::error::{problem}", file=sys.stderr)

    if problems:
        return 1

    print(f"{args.label}: {total} tests ran, none skipped, at its pin.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
