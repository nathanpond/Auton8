#!/bin/sh
# Generate the release assets: substitute the published image digests and the
# version into the templates.
#
# Every __DIGEST_*__ placeholder must be replaced. An unsubstituted one would
# ship a compose file that cannot start, so this fails rather than emitting
# one — the check is the point, not a formality.
#
# Usage:
#   infra/release/generate.sh <version> <out-dir>
# Digests come from the environment, one per image:
#   DIGEST_AUTONATEWEB DIGEST_HOCUSPOCUS DIGEST_EXECUTOR DIGEST_FLOWABLE
#   DIGEST_POSTGRES DIGEST_REDIS DIGEST_NATS DIGEST_NATSBOX
#   DIGEST_DAPRD DIGEST_PLACEMENT DIGEST_SCHEDULER
set -eu

VERSION="${1:?usage: generate.sh <version> <out-dir>}"
OUT="${2:?usage: generate.sh <version> <out-dir>}"
HERE=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)

mkdir -p "$OUT"

substitute() {
    sed \
        -e "s|__VERSION__|${VERSION}|g" \
        -e "s|__DIGEST_AUTONATEWEB__|${DIGEST_AUTONATEWEB:-}|g" \
        -e "s|__DIGEST_HOCUSPOCUS__|${DIGEST_HOCUSPOCUS:-}|g" \
        -e "s|__DIGEST_EXECUTOR__|${DIGEST_EXECUTOR:-}|g" \
        -e "s|__DIGEST_FLOWABLE__|${DIGEST_FLOWABLE:-}|g" \
        -e "s|__DIGEST_POSTGRES__|${DIGEST_POSTGRES:-}|g" \
        -e "s|__DIGEST_REDIS__|${DIGEST_REDIS:-}|g" \
        -e "s|__DIGEST_NATS__|${DIGEST_NATS:-}|g" \
        -e "s|__DIGEST_NATSBOX__|${DIGEST_NATSBOX:-}|g" \
        -e "s|__DIGEST_DAPRD__|${DIGEST_DAPRD:-}|g" \
        -e "s|__DIGEST_PLACEMENT__|${DIGEST_PLACEMENT:-}|g" \
        -e "s|__DIGEST_SCHEDULER__|${DIGEST_SCHEDULER:-}|g" \
        "$1" > "$2"
}

substitute "$HERE/compose.template.yml" "$OUT/compose.yml"
substitute "$HERE/env.template"         "$OUT/env.template"
substitute "$HERE/QUICKSTART.md"        "$OUT/QUICKSTART.md"

# Nothing may ship with a placeholder still in it.
if grep -l '__DIGEST_\|__VERSION__' "$OUT"/* >/dev/null 2>&1; then
    echo "generate.sh: unsubstituted placeholders remain — refusing to emit a broken release." >&2
    grep -n '__DIGEST_\|__VERSION__' "$OUT"/* >&2
    exit 1
fi

# Every image reference must carry a digest. A bare tag here would undo the
# point of the whole release: an immutable stack a consumer can verify.
if grep -nE '^\s+image: [^@]+$' "$OUT/compose.yml" >&2; then
    echo "generate.sh: an image is not pinned by digest." >&2
    exit 1
fi

echo "generate.sh: wrote compose.yml, env.template and QUICKSTART.md to $OUT"
