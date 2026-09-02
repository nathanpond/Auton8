# Contributing to Auton8

Thanks for looking. A few things about this repository are not obvious from
the outside, and knowing them first will save you a wasted afternoon.

## How work gets planned here

**GitHub Issues are the plan, not a suggestion box.** This project is managed
with an issue-driven workflow: issues are grouped into milestones, carry
`sev:*` and `area:*` labels, and are closed by pull requests that reference
them (`Closes #123`). Roadmap and audit passes file issues in bulk, so the
register is the authoritative picture of what is known to be wrong.

Blank issues are turned off deliberately — `.github/ISSUE_TEMPLATE/` has forms
for **bug**, **story**, and **epic**, and each applies its own label. Use the
form that fits. If none fits, a bug report describing what you observed is
always acceptable.

**Security findings do not go in issues.** See [SECURITY.md](SECURITY.md).

**`#N` in an old commit message is not an issue in this repository.** The
project moved repositories on 2026-09-02 (see
[docs/history/](docs/history/README.md) for why), and issue numbers were not
preserved. References in commits written before that date point at the archived
register, not at the current one — and may coincide with a different, real
issue here.

## Proposing a change

For anything beyond an obvious fix, **open an issue before writing code.** Not
ceremony: the roadmap is planned in milestones, and a PR that arrives against
a plan nobody knew about is hard to place. A short issue saying what you want
to change and why gets you an answer quickly.

Small and self-contained — a typo, a broken link, a clearly wrong condition
with an obvious fix — go straight to a PR.

## Working on the code

Build and run instructions live in [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md);
deployment lives in [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md).

Before opening a PR, run what CI runs:

```bash
# SPA — lint carries a warning ratchet and a jsx-a11y error gate
cd src/AutoNate.Spa && npm ci && npm run lint && npx tsc -b && npm run build

# Backend — needs Postgres, NATS and Redis up
cd infra && docker compose -p infra up -d postgres nats nats-init redis
cd .. && dotnet test tests/AutoNate.Web.Tests
```

The end-to-end suite (`tests/AutoNate.E2E.Tests`) needs the full compose stack
including Flowable and the Hocuspocus sidecar. CI runs it with the specs that
need Flowable and Dapr filtered out; if your change touches those areas, say
so in the PR so it gets run somewhere that has them.

### What a good change looks like here

- **Match the surrounding code.** Naming, comment density, and idiom vary by
  area; the local convention wins over a global preference.
- **A bug fix comes with a test that fails without it.** Not as a formality —
  write the test, revert the fix, and confirm it actually goes red. A
  surprising number of assertions cannot fail; that check is how you find out
  yours can.
- **Accessibility is enforced, not aspirational.** `npm run lint` fails on
  jsx-a11y errors in the directories that are already clean. Interactive
  elements need accessible names — several test suites locate elements by
  role and name, so an unnamed control is both an a11y defect and a broken
  test locator.
- **Comments should explain why, not what.** The tree leans heavily on this.
  If a line looks odd and is deliberate, the reason belongs next to it.

## Naming: Auton8 vs AutoNate

The product is **Auton8**. Internally the code still says **AutoNate** —
namespaces, assembly names, the `AutoNateDbContext`, `AUTONATE_*` environment
variables, database and schema names, plugin ABI types, and the document and
BPMN markers.

This is deliberate, not leftover. Those identifiers are load-bearing: renaming
the DataProtection purpose strings makes every stored provider secret
undecryptable, renaming the `.docx` markers orphans every bound document, and
renaming the plugin ABI breaks third-party plugins. Please do not "fix" them.

New user-facing strings should say Auton8. New internal identifiers should
follow whatever their neighbours do.

## Licensing

By contributing you agree your contributions are licensed under the
[Apache License 2.0](LICENSE), the same as the rest of the project.
