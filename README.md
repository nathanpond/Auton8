# Auton8

A self-hosted business automation platform: model a process, define the data
it works on, and give people the screens to work it.

Most automation tools stop at the workflow engine and leave you to build the
application around it. Auton8 ships both halves — a BPMN engine and the
records, documents, forms, dashboards and query tools that a process actually
runs on — behind one administrable UI.

> **Status: 0.1.** Early, and honest about it. It runs, it is tested, and it
> is in use — but interfaces move, and there is no upgrade-compatibility
> promise between 0.x releases yet.

## What's in it

- **Workflows** — a BPMN modeller and a [Flowable](https://www.flowable.com/)
  engine behind it. Draft, deploy, launch, and trace runs step by step.
- **Records** — define record types and fields at runtime, then search, edit,
  relate, comment on, and watch the records built on them. No redeploy to add
  a field.
- **Documents** — collaborative editing with tracked changes and comments,
  DOCX import/export, and bindings that pull live record data into a document.
- **Notes and pages** — real-time collaborative rich text, backed by
  [Yjs](https://yjs.dev/).
- **Data stores, datasets and pipelines** — connect SQL and file-backed
  sources, preview and profile them, and move data through code transformers.
- **Queries and dashboards** — AQL over records and workflow state, surfaced
  as configurable widgets.
- **Forms** — define forms and map them onto record types.
- **An AI assistant** — an in-app agent with tool access to the platform's own
  APIs, pointed at a model you configure.
- **Administration** — role- and instance-level authorization, groups,
  permission explain ("why can this user do that?"), configurable menus and
  pages, a plugin system, an event bus watcher, and system health.

## Stack

ASP.NET Core 10 host, React 19 + Vite + TypeScript SPA on Mantine v9,
PostgreSQL, Flowable, Dapr with NATS JetStream and Redis, and Node sidecars
for collaboration ([Hocuspocus](https://tiptap.dev/docs/hocuspocus)) and
sandboxed code execution.

## Quickstart

### Run it

Docker is the only prerequisite. From the
[latest release](https://github.com/nathanpond/Auton8/releases/latest),
download `compose.yml` and `env.template` into an empty directory:

```bash
mv env.template .env
# Set Bootstrap__AdminUsername and Bootstrap__AdminPassword, and generate the
# three secrets the file asks for. Nothing is seeded: without an administrator
# there is no way to sign in.

docker compose up -d
```

Then open http://localhost:5108. Every image is pinned by digest and carries a
signed provenance attestation — `QUICKSTART.md` in the release shows how to
verify one.

**Auton8 1.0 requires a fresh database.** Upgrading a 0.x install is not
supported; releases after 1.0 will carry upgrade paths.

### Develop it

Requires Docker, the .NET 10 SDK, Node 24 and the Dapr CLI — the authoritative
list with versions is [`infra/prerequisites`](infra/prerequisites), and
`make preflight` checks all of them at once.

```bash
git clone https://github.com/nathanpond/Auton8.git
cd Auton8

make infra-up    # Postgres, Flowable, Redis, NATS, the Dapr control plane
                 # (runs `make preflight` first and stops if anything is missing)

export Bootstrap__AdminUsername=admin
export Bootstrap__AdminPassword='pick something'

make app
```

`make app-container` runs the application as a container instead, if you would
rather not install the SDK toolchain. `make infra-down` stops the stack;
`make infra-reset` throws away its data.

## Documentation

- [Development](docs/DEVELOPMENT.md) — the local stack, daily workflow,
  configuration, and how to run the tests
- [Deployment](docs/DEPLOYMENT.md) — required overrides, TLS and reverse
  proxy, runtime storage, and a pre-deployment checklist
- [Contributing](CONTRIBUTING.md) — how work is planned here and what a good
  change looks like
- [Security](SECURITY.md) — how to report a vulnerability privately
- [Codebase map](https://github.com/nathanpond/Auton8/wiki) — architecture,
  structure, conventions, testing and known concerns, in the wiki

## A note on the name

The product is **Auton8**. The code says **AutoNate** — namespaces, assembly
names, environment variables, database and schema names, plugin ABI types.
That is deliberate: those identifiers are load-bearing, and renaming them
would make stored secrets undecryptable and orphan existing documents.
[CONTRIBUTING.md](CONTRIBUTING.md#naming-auton8-vs-autonate) has the details.

## Licence

[Apache-2.0](LICENSE).

Third-party components keep their own licences — notably
[bpmn-js](https://github.com/bpmn-io/bpmn-js), whose "Powered by bpmn.io"
attribution must be preserved in any deployment of the workflow modeller.
