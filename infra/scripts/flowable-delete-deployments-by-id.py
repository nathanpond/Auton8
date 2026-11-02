#!/usr/bin/env python3
"""Delete Flowable deployments by explicit id -- and by nothing else (#301).

Why this exists
---------------
`FlowableDeploymentSweep` (tests/AutoNate.E2E.Tests/Support) removes suite
deployments by the `e2e-` prefix that `Flowable__DeploymentNamePrefix` stamps.
That only works going forward. Deployments the suite made BEFORE the prefix
(#257) are named after raw process keys -- `esp_*`, `mi*`, `tb_*`, `swp*` -- and
can never match it. #301 counted 21 of them on the shared dev engine, all from
2026-09-10 13:22-13:41 UTC.

They must not be caught by widening the sweep. A name or age rule is exactly what
deleted 348 deployments of real work under #257 and what cascades over
concurrent runs under #297. The honest cleanup is a human reading a list and
naming ids, so that is the only thing this tool deletes.

Usage
-----
    # 1. Show candidates: deployments in a time window whose names do NOT carry
    #    the suite prefix (those are already the sweep's job). Prints
    #    `id<TAB>deploymentTime<TAB>name`. Deletes nothing.
    infra/scripts/flowable-delete-deployments-by-id.py list \
        --from 2026-09-10T13:22:00Z --to 2026-09-10T13:42:00Z

    # 2. Read the list. Remove anything that is not suite scratch.

    # 3. Delete exactly the ids you kept. `--yes` is required; without it the
    #    tool prints what it would delete and stops.
    infra/scripts/flowable-delete-deployments-by-id.py delete --yes <id> [<id> ...]

`list` only ever proposes. `delete` never consults a name, an age or a
pattern -- it takes the ids it was given, cascades each one (definitions,
instances and history go with it, as in the sweep), and reports per id.

Engine: AUTONATE_FLOWABLE_URL / _USER / _PASSWORD, defaulting to the same values
the E2E fixture uses (http://localhost:8080/flowable-rest, rest-admin / test).
"""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

SUITE_PREFIX = "e2e-"  # FlowableDeploymentSweep.SuiteDeploymentPrefix
PAGE_SIZE = 500        # FlowableDeploymentSweep.PageSize


def _engine() -> tuple[str, str]:
    base = os.environ.get("AUTONATE_FLOWABLE_URL", "http://localhost:8080/flowable-rest").rstrip("/")
    user = os.environ.get("AUTONATE_FLOWABLE_USER", "rest-admin")
    password = os.environ.get("AUTONATE_FLOWABLE_PASSWORD", "test")
    token = base64.b64encode(f"{user}:{password}".encode()).decode()
    return base, f"Basic {token}"


def _request(method: str, path: str) -> tuple[int, bytes]:
    base, auth = _engine()
    req = urllib.request.Request(f"{base}/{path}", method=method, headers={"Authorization": auth})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as err:
        return err.code, err.read()
    except urllib.error.URLError as err:
        sys.exit(f"cannot reach the engine at {base}: {err.reason}")


def _parse_time(value: str) -> dt.datetime:
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=dt.timezone.utc)


def _all_deployments() -> list[dict]:
    # Paged until a short page, as the sweep does since #537: one page is not the
    # population.
    rows: list[dict] = []
    start = 0
    while True:
        query = urllib.parse.urlencode({"start": start, "size": PAGE_SIZE, "sort": "deployTime", "order": "asc"})
        status, body = _request("GET", f"service/repository/deployments?{query}")
        if status != 200:
            sys.exit(f"listing failed at offset {start}: HTTP {status}: {body[:200]!r}")
        page = json.loads(body).get("data", [])
        rows.extend(page)
        if len(page) < PAGE_SIZE:
            return rows
        start += PAGE_SIZE


def cmd_list(args: argparse.Namespace) -> int:
    lo, hi = _parse_time(args.from_), _parse_time(args.to)
    shown = 0
    for row in _all_deployments():
        name = row.get("name") or ""
        when = row.get("deploymentTime")
        if not when or name.startswith(SUITE_PREFIX):
            continue
        if lo <= _parse_time(when) < hi:
            print(f"{row['id']}\t{when}\t{name}")
            shown += 1
    print(f"# {shown} candidate(s) in [{args.from_}, {args.to}). Nothing was deleted.", file=sys.stderr)
    return 0


def cmd_delete(args: argparse.Namespace) -> int:
    if not args.yes:
        for deployment_id in args.ids:
            print(f"would delete {deployment_id}")
        print("# dry run: pass --yes to delete exactly these ids.", file=sys.stderr)
        return 0

    failed = 0
    for deployment_id in args.ids:
        status, body = _request(
            "DELETE", f"service/repository/deployments/{urllib.parse.quote(deployment_id, safe='')}?cascade=true")
        if status in (200, 204):
            print(f"deleted {deployment_id}")
        else:
            failed += 1
            print(f"FAILED {deployment_id}: HTTP {status}: {body[:200]!r}", file=sys.stderr)
    return 1 if failed else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)

    listing = sub.add_parser("list", help="print candidate deployments in a time window; deletes nothing")
    listing.add_argument("--from", dest="from_", required=True, help="inclusive, ISO-8601 (UTC if no offset)")
    listing.add_argument("--to", required=True, help="exclusive, ISO-8601 (UTC if no offset)")
    listing.set_defaults(func=cmd_list)

    deleting = sub.add_parser("delete", help="cascade-delete exactly the given deployment ids")
    deleting.add_argument("ids", nargs="+", metavar="ID")
    deleting.add_argument("--yes", action="store_true", help="actually delete; otherwise a dry run")
    deleting.set_defaults(func=cmd_delete)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
