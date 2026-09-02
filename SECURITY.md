# Security Policy

## Reporting a vulnerability

**Please do not open a public issue.** A public reproduction is a working
exploit handed to everyone running the software, including people who cannot
patch today.

Report through GitHub's private vulnerability reporting instead: go to the
**Security** tab → **Report a vulnerability**. That opens a private thread
visible only to maintainers, and it is the only channel that stays private
from the first message.

Useful things to include, in rough order of value:

- what an attacker gets — read another tenant's records, escalate to
  SuperAdmin, run code on the host
- the smallest request or sequence that reaches it, ideally from an
  unauthenticated or low-privilege starting point
- the commit or version you tested
- whether it needs a specific configuration (authorization enforcement mode,
  a plugin installed, a particular data-store backend)

A partial report beats a delayed one. If you are unsure whether something is a
vulnerability or a bug, report it privately and let us sort it out.

## What to expect

This is a small project. Rather than promise a response time it cannot keep,
here is the honest version:

- **Acknowledgement** — usually within a few days. If a week passes with no
  reply, assume the notification was missed and ping the thread.
- **Assessment** — we will tell you whether we can reproduce it, and what
  severity we think it carries, using the same scale as the internal audits:
  critical (exploitable and destructive today), high (exploitable or badly
  wrong under realistic use), medium (wrong where a clean failure was owed),
  low (hardening).
- **Fix** — critical and high findings are worked ahead of feature work.
  Lower severities are queued and may wait for a release.
- **Disclosure** — we publish an advisory when a fix ships. Tell us how you
  want to be credited, or that you would rather not be.

There is no bug bounty.

## Scope

In scope: this repository — the ASP.NET Core host, the SPA, the Node sidecars
under `services/`, the plugin ABI, and the deployment material under `infra/`.

Out of scope, though still worth telling us about informally:

- findings that require an already-compromised host or database
- the local development compose stack's deliberately weak defaults — it binds
  to loopback and ships dev credentials on purpose; a deployment that exposes
  it is a deployment mistake, not a vulnerability in this repo
- dependency CVEs with no demonstrated path through this code (Dependabot
  already files those)

## A note on this project's own findings

Security issues found by the project's own audits are tracked in this
repository. Historically they were tracked as labelled issues because the
repository was private, so those were already maintainer-only; issues filed
under that arrangement remain visible in the register. New findings are
handled through advisories.
