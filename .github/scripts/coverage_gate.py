#!/usr/bin/env python3
"""Apply the coverage ratchet to a merged Cobertura report and render the PR comment.

Reads the merged report, compares line coverage against the threshold, and
writes the markdown that goes on the pull request.

Two properties matter and are easy to lose:

  * The threshold is applied to the MERGED report. Applied per shard it would
    measure a tenth of the code against a whole-codebase number and mean
    nothing.
  * Both line and branch coverage are reported even though only line is gated.
    The ungated number is what tells you whether the gated one is telling the
    truth -- 90% line with 40% branch is a suite that runs code without
    checking it.
"""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def read_rates(path: Path) -> tuple[float, float, int, int]:
    """Line rate, branch rate, and the covered/total line counts."""
    root = ET.parse(path).getroot()

    # Cobertura puts rates on the root as fractions. Recomputing from the
    # counters instead would double-count classes that appear in several
    # shards' reports; ReportGenerator has already deduplicated them here.
    line_rate = float(root.get("lines-covered") or 0), float(root.get("lines-valid") or 0)
    covered, valid = int(line_rate[0]), int(line_rate[1])

    lr = float(root.get("line-rate") or 0) * 100
    br = float(root.get("branch-rate") or 0) * 100
    return lr, br, covered, valid


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--report", required=True, help="Merged Cobertura XML.")
    ap.add_argument("--threshold", type=float, required=True)
    ap.add_argument("--comment-file", default="")
    ap.add_argument("--summary-file", default="")
    ap.add_argument(
        "--report-only",
        action="store_true",
        help="Print the numbers and always exit 0. Used to establish the "
        "ratchet's starting value from a real run before it is enforced.",
    )
    args = ap.parse_args()

    report = Path(args.report)
    if not report.is_file():
        print(
            "::error::No merged coverage report was produced. Coverage is "
            "unmeasured, which is indistinguishable from coverage being zero.",
            file=sys.stderr,
        )
        return 1

    line, branch, covered, valid = read_rates(report)
    passed = line >= args.threshold
    delta = line - args.threshold

    status = "PASS" if passed else "FAIL"
    if args.report_only:
        status = "measuring"

    lines = [
        "## Coverage",
        "",
        "| metric | value |",
        "|---|---|",
        f"| line coverage | **{line:.2f}%** ({covered:,} / {valid:,} lines) |",
        f"| branch coverage | {branch:.2f}% |",
        f"| threshold (line) | {args.threshold:.2f}% |",
        f"| result | **{status}** |",
        "",
    ]

    if args.report_only:
        lines.append(
            "> Measuring only — the ratchet is not being enforced on this run."
        )
    elif passed:
        lines.append(f"> {delta:+.2f}% against the threshold.")
    else:
        lines.append(
            f"> **Coverage fell below the ratchet by {abs(delta):.2f}%** "
            f"({line:.2f}% against a threshold of {args.threshold:.2f}%). "
            "Add tests for the new code, or — if the drop is genuinely correct, "
            "such as deleting well-covered code — say so and adjust the ratchet "
            "deliberately. Do not lower it to make this build pass."
        )

    lines += [
        "",
        "<sub>Line coverage is gated; branch coverage is reported because it is "
        "what tells you whether the gated number is telling the truth. Merged "
        "across all backend shards.</sub>",
    ]

    body = "\n".join(lines) + "\n"

    if args.comment_file:
        Path(args.comment_file).write_text(body)
    if args.summary_file:
        with Path(args.summary_file).open("a") as fh:
            fh.write(body)

    print(f"line={line:.2f} branch={branch:.2f} threshold={args.threshold:.2f} {status}")

    if args.report_only or passed:
        return 0

    print(
        f"::error::Coverage {line:.2f}% is below the ratchet of {args.threshold:.2f}% "
        f"(down {abs(delta):.2f}%).",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
