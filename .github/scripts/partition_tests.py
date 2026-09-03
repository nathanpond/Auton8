#!/usr/bin/env python3
"""Partition the backend test suite into deterministic shards.

Reads the output of `dotnet test --list-tests` and emits, as JSON, one
`--filter` expression per shard plus the total number of test cases discovered.
The total is what the reconciliation job checks the shards' executed counts
against; if that number is wrong, every CI run fails, so this file is tested
(tests/AutoNate.Web.Tests/Infrastructure/TestPartitionScriptTests.cs).

Two properties are required of the partition and neither is negotiable:

  deterministic  the same class always lands in the same shard, so a flaky
                 failure is reproducible by re-running one shard.
  automatic      a new test class is assigned with no workflow edit. Any
                 hand-maintained list drifts, and its drift is invisible.

md5(fully-qualified class name) % shard_count satisfies both. A weighted
partition was tried and rejected: correlation between a class's test count and
its duration measured 0.305, and greedy longest-processing-time on that weight
came out no better than the hash.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys

# `dotnet test --list-tests` indents each test case by four spaces under a
# "The following Tests are available:" header.
ROW = re.compile(r"^ {4}(\S.*)$")


def parse_rows(text: str) -> list[str]:
    """Every discovered test case, verbatim."""
    return [m.group(1) for line in text.splitlines() if (m := ROW.match(line))]


def class_of(row: str) -> str:
    """The declaring class of one `--list-tests` row.

    Theory cases arrive with their arguments attached:

        Ns.KindGateEnforcementTests.Route_IsForbidden(route: "/api/x/", ...)

    Those arguments can contain dots, parentheses and quotes, so the arguments
    are cut at the first '(' *before* the method name is stripped. Getting this
    wrong is not a cosmetic bug: a parse that expects `Namespace.Class.Method`
    drops every theory row, which silently lost all 24 tests of
    KindGateEnforcementTests -- one of the guards CLAUDE.md names as enforcing
    project invariant 3 -- and left a partition that looked entirely healthy.
    """
    return row.split("(", 1)[0].rsplit(".", 1)[0]


def find_prefix_collisions(classes: list[str]) -> list[tuple[str, str]]:
    """Class names where one is a prefix of another.

    `--filter FullyQualifiedName~X` is a substring match, so if one class name
    prefixes another the shorter one's filter also selects the longer one's
    tests and they run in two shards. Emitted filters carry a trailing '.' to
    prevent it, but a collision is reported as a hard error anyway: it means
    two shards disagree about who owns a class, and the reconciliation total
    would start drifting for a reason nobody would look for here.
    """
    ordered = sorted(set(classes))
    return [(a, b) for a in ordered for b in ordered if a != b and b.startswith(a)]


def shard_of(class_name: str, shard_count: int) -> int:
    digest = hashlib.md5(class_name.encode("utf-8")).hexdigest()
    return int(digest, 16) % shard_count


def partition(text: str, shard_count: int) -> dict:
    rows = parse_rows(text)
    if not rows:
        raise SystemExit(
            "No test cases found in the --list-tests output. Refusing to emit an "
            "empty partition: every shard would filter to nothing and the build "
            "would go green having run no tests."
        )

    classes = [class_of(r) for r in rows]

    collisions = find_prefix_collisions(classes)
    if collisions:
        detail = "\n".join(f"  '{a}' is a prefix of '{b}'" for a, b in collisions)
        raise SystemExit(
            "Test class names collide by prefix, so a substring filter cannot "
            f"separate them:\n{detail}\n"
            "Rename one of them, or teach the filter to match exactly."
        )

    buckets: list[list[str]] = [[] for _ in range(shard_count)]
    for name in sorted(set(classes)):
        buckets[shard_of(name, shard_count)].append(name)

    counts = {}
    for name in classes:
        counts[name] = counts.get(name, 0) + 1

    shards = []
    for index, names in enumerate(buckets):
        shards.append(
            {
                "index": index,
                # Trailing '.' so a class matches only its own methods.
                "filter": "|".join(f"FullyQualifiedName~{n}." for n in names),
                "classes": len(names),
                "tests": sum(counts[n] for n in names),
            }
        )

    empty = [s["index"] for s in shards if s["classes"] == 0]
    if empty:
        raise SystemExit(
            f"Shards {empty} would run no tests. That is a wasted job at best "
            "and a hidden partition bug at worst -- lower the shard count "
            f"below {shard_count}."
        )

    return {
        "total_tests": len(rows),
        "total_classes": len(set(classes)),
        "shard_count": shard_count,
        "shards": shards,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--shards", type=int, required=True)
    ap.add_argument(
        "--list-tests",
        default="-",
        help="File holding `dotnet test --list-tests` output, or - for stdin.",
    )
    ap.add_argument(
        "--emit",
        choices=("json", "matrix", "total", "summary"),
        default="json",
        help="json: the whole partition. matrix: just the shard array, for "
        "fromJSON(). total: the expected test count. summary: a markdown "
        "table for $GITHUB_STEP_SUMMARY.",
    )
    args = ap.parse_args()

    if args.shards < 1:
        raise SystemExit("--shards must be at least 1.")

    text = sys.stdin.read() if args.list_tests == "-" else open(args.list_tests).read()
    result = partition(text, args.shards)

    if args.emit == "summary":
        print("### Backend partition")
        print()
        print(f"{result['total_tests']} test cases in "
              f"{result['total_classes']} classes across "
              f"{result['shard_count']} shards.")
        print()
        print("| shard | classes | tests |")
        print("|---|---|---|")
        for s in result["shards"]:
            print(f"| {s['index']} | {s['classes']} | {s['tests']} |")
    elif args.emit == "matrix":
        print(json.dumps(result["shards"], separators=(",", ":")))
    elif args.emit == "total":
        print(result["total_tests"])
    else:
        print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
