---
name: cut-a-release
description: Cut an Auton8 release — bump the version, tag, watch the publish workflow, verify provenance from outside it, smoke-test the released compose file, and write the notes. Use when asked to "cut a release", "publish a release", "ship <version>", "tag a release", or to fix a release that went wrong.
---

# Cutting an Auton8 release

Pushing a `v*` tag is the whole act: `.github/workflows/release.yml` builds four
multi-arch images, publishes them to GHCR, attests their provenance, and attaches
a digest-pinned `compose.yml` to the GitHub release.

Nothing here is hard. All of it is forgettable, and it is performed rarely enough
that nobody builds a habit — which is why it is written down.

**Treat every path and command below as a claim that may have rotted.** Verify it
against the tree before following it, and if code has moved, fix this skill in the
same commit as the change. "Later" does not happen.

## 1. Agree the version — it lives in two files

```bash
grep '<Version>' Directory.Build.props
grep '"version"' src/AutoNate.Spa/package.json
```

They must match. `Directory.Build.props` carries a comment saying to keep them in
step, and the release workflow's first job **validates the tag against
`Directory.Build.props` and fails before publishing anything** — deliberately,
because a digest is immutable and a consumed tag cannot be taken back.

A prerelease suffix is allowed: tag `v1.2.0-rc1` validates against `1.2.0`.

Bump both, commit.

## 2. Check master is green

```bash
gh run list --workflow ci.yml --branch master --limit 1
```

The release workflow does not run the test suite. It builds and publishes what
you point it at.

## 3. Tag and push

```bash
git tag -a v1.2.0 -m "Auton8 1.2.0"
git push origin v1.2.0
```

## 4. Watch it

```bash
gh run watch "$(gh run list --workflow release.yml --limit 1 --json databaseId --jq '.[0].databaseId')" --interval 30
```

Expect **arm64 to be slow**. It is built under QEMU emulation and the times are
very uneven — measured on the first real run:

| image | cold | warm (cached) |
|---|---|---|
| hocuspocus | 2m 42s | 29s |
| executor | 2m 52s | 29s |
| flowable | 5m 19s | 54s |
| **autonate-web** | **48m 51s** | 36s |

A cold `autonate-web` build approaching an hour is expected, not a hang. If that
becomes intolerable, native ARM runners are the remedy.

## 5. Verify provenance from outside the workflow

The workflow verifies its own attestations, and that is not the same as you
verifying them. Do this from your machine:

```bash
BASE=ghcr.io/nathanpond/auton8
for img in autonate-web hocuspocus executor flowable; do
  d=$(docker buildx imagetools inspect "$BASE/$img:1.2.0" | awk '/^Digest:/{print $2; exit}')
  gh attestation verify "oci://$BASE/$img@$d" --repo nathanpond/Auton8 --format json \
    | python3 -c "import json,sys; a=json.load(sys.stdin)[0]['verificationResult']; \
        print('$img', a['statement']['predicateType'], a['signature']['certificate']['sourceRepositoryURI'])"
done
```

Expect `https://slsa.dev/provenance/v1` and `https://github.com/nathanpond/Auton8`.

`gh attestation verify` **exits 0 and prints nothing on success** in some
versions. Do not read silence as failure or as success — check the exit code, or
use `--format json` as above and read the result.

Confirm both platforms are really there:

```bash
docker buildx imagetools inspect "$BASE/autonate-web:1.2.0" | grep -oE 'linux/(amd64|arm64)' | sort -u
```

## 6. Smoke-test the released stack

The point of a release is that a stranger can run it. Prove that, in an empty
directory, with nothing from the source tree:

```bash
mkdir /tmp/auton8-smoke && cd /tmp/auton8-smoke
gh release download v1.2.0 --pattern 'compose.yml' --pattern 'env.template'
mv env.template .env
# Fill in Bootstrap__AdminUsername / Bootstrap__AdminPassword and generate:
#   POSTGRES_PASSWORD, WORKFLOW_CALLBACK_SECRET, YJS_SHARED_SECRET
docker compose -p auton8smoke up -d
```

Then check it actually works, not just that containers started:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5108/api/health/live   # 200
```

And sign in. **This is fiddly and worth getting right**: `/account/login`
requires an antiforgery token — deliberately, against login CSRF — so a plain
POST returns 400 and that is correct behaviour, not a bug:

```bash
AF=$(curl -s -c /tmp/cj http://127.0.0.1:5108/api/auth/antiforgery)
TOKEN=$(echo "$AF" | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])")
FIELD=$(echo "$AF" | python3 -c "import sys,json; print(json.load(sys.stdin)['formFieldName'])")
curl -s -b /tmp/cj -c /tmp/cj -X POST http://127.0.0.1:5108/account/login \
  --data-urlencode "$FIELD=$TOKEN" \
  --data-urlencode "username=$USER" --data-urlencode "password=$PASS"
curl -s -b /tmp/cj http://127.0.0.1:5108/api/auth/me   # {"authenticated":true,...}
```

Tear it down: `docker compose -p auton8smoke down --volumes`.

## 7. Write the notes

`--generate-notes` gives a commit list. Replace the top of it with what a reader
must know **before** upgrading. The 0.1.0 notes are the model: they led with the
committed-credential disclosure because that was the thing an existing operator
had to act on.

For 1.0 specifically, lead with **"requires a fresh database"** — upgrading a 0.x
install is not supported.

## When it goes wrong

**A publish job failed.** Fix the cause, then re-run on the same tag — the
workflow is re-runnable and GHCR accepts overwriting a tag. `git tag -f` and
`git push -f origin <tag>` if the fix is a code change.

**The push succeeded and verification failed.** This has happened: the repository
is `nathanpond/Auton8` with a capital A, OCI names must be lowercase, and
`docker/metadata-action` lowercases silently while anything using the raw
`github.repository` does not. Four images published and four verifications
failed. Do not "fix" it by dropping the verify step.

**You need to undo a release.** Delete the tag, the release, and the packages —
all three, or the images stay pullable:

```bash
git push --delete origin v1.2.0 && git tag -d v1.2.0
gh release delete v1.2.0 --yes
for img in autonate-web hocuspocus executor flowable; do
  gh api -X DELETE "user/packages/container/auton8%2F$img"
done
```

Package deletion needs `delete:packages` scope, which a default `gh` token does
not carry: `gh auth refresh -h github.com -s read:packages,delete:packages`.

## What this does not do

It publishes; it does not deploy. Auton8 v1.0 ships artifacts other people run.


## Corrections (audit, 2026-09-05)

⚠️ **Do NOT run `gh release create`.** The tag-triggered workflow already does it, in
the `assets` job:

```bash
gh release create "$GITHUB_REF_NAME" --title "Auton8 <version>" --generate-notes --verify-tag \
  2>/dev/null || echo "release already exists; attaching to it"
```

Running it yourself either races the workflow or gets silently swallowed by that
fallback, and it is then non-obvious whose notes survived. To add a summary, use
`gh release edit vX.Y.Z --notes-file notes.md` on the release the workflow made.

**When the publish job fails, re-run it before changing anything.** The one failure
that has actually happened was `apt-get update` exiting 100 against a stale Debian
mirror (`src/AutoNate.Web/Dockerfile:71`, issue #143 — still open, no retry logic).
It is transient and external; a plain re-run of the failed job succeeded. Two things
to know while diagnosing: `Release assets` is gated on the publishes, so **no release
object exists at all** — a `gh release download` will 404 and look like a different
problem — and some images may already have been pushed under that tag.

**Expect ~45 minutes, not the warm-cache figure.** Real runs: v0.2.0 43m52s,
v0.1.1-rc1 47m11s. Only an immediate re-tag hits the warm path, because GHA cache
scopes evict between releases. Reading a normal build as a hang is the mistake.

**The SPA version is on the honour system.** The workflow validates only
`Directory.Build.props` against the tag; nothing checks
`src/AutoNate.Spa/package.json`, and no test does either.

**The release ships three assets, not two** — `compose.yml`, `env.template` **and
`QUICKSTART.md`**. Since the smoke test exists to prove a stranger can run it, follow
QUICKSTART.md rather than a hand-written recipe that can drift from it.

**Smoke-test Production too.** The documented run uses the template default
`APP_ENVIRONMENT=Development`, which the template itself describes as permissive
hosts and relaxed startup checks. Add a pass with `APP_ENVIRONMENT=Production` and a
real `ALLOWED_HOSTS` — that is the path an operator actually uses.

**`release.yml` is shape-tested.** `ReleaseWorkflowTests` pins
`cancel-in-progress: false`, per-job permissions, all four images, both platforms, no
moving tag, the lowercasing step and in-run attestation verification. Editing the
workflow to fix a release will trip them, and that is the point.

**Relationship to `/n8-release`:** that command owns the generic half — preconditions,
version choice, tagging, watching the workflow, the decision log. This skill owns what
it cannot know: the two version files, the lowercase-GHCR incident, the external
`gh attestation verify` loop, the released-compose smoke test, and the three-part undo.
Use `/n8-release` to cut; use this to verify and to recover.

<!-- verify-ignore: compose.yml env.template QUICKSTART.md -->
