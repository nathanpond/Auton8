#!/usr/bin/env bash
# Print the absolute path of the compose bind-mount root.
#
# Every worktree resolves to the SAME directory: the one belonging to the main
# checkout. That is the whole point (#505).
#
# `infra/mounts/**` is gitignored, so in a fresh `git worktree` it does not
# exist. Compose resolves relative bind mounts against the compose file's own
# directory, so a `make test-full-local` run from a worktree used to stand the
# stack up on an EMPTY data directory -- and, because the compose project name
# is `infra` either way, it did not start a second stack. It replaced the
# developer's. Postgres came up as a brand-new cluster and Flowable came up
# with no schema at all:
#
#   Caused by: org.postgresql.util.PSQLException:
#     ERROR: relation "act_ru_job" does not exist
#
# The measured result was 161 of 407 E2E tests failing with "The workflow engine
# refused this workflow", which reads exactly like a product regression and is
# not one. `/n8-verify` runs in worktrees by instruction, so the environment
# that checks for defects was the one environment the tier could not run in.
#
# `--path-format=absolute` (git 2.31+) matters: the bare `--git-common-dir`
# is relative to the CWD (`.git` from the root, `../.git` from `infra/`), so
# composing it with `..` by hand is wrong from anywhere but the top.
set -uo pipefail

if common=$(git rev-parse --path-format=absolute --git-common-dir 2>/dev/null) \
   && [ -n "$common" ]; then
    # `--git-common-dir` is the MAIN checkout's .git even when called from a
    # linked worktree, which is what makes every worktree agree on one root.
    printf '%s\n' "$(cd "$common/.." && pwd)/infra/mounts"
    exit 0
fi

# Not a git checkout at all -- a release tarball, or a stack copied somewhere.
# Fall back to the directory next to this script, which is the pre-#505
# behaviour and correct when there are no worktrees to disagree with.
printf '%s\n' "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/mounts"
