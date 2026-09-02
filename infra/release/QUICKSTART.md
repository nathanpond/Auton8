# Running Auton8 __VERSION__

Docker is the only prerequisite. No clone, no build, no toolchain.

## 1. Get the two files

From the [__VERSION__ release](https://github.com/nathanpond/Auton8/releases/tag/v__VERSION__),
download `compose.yml` and `env.template` into an empty directory, and rename
the second one:

```bash
mv env.template .env
```

## 2. Fill in `.env`

Two things are required and neither has a default.

**A first administrator.** Nothing is seeded — there is no registration page
and no setup wizard, so without this there is no way to sign in:

```
Bootstrap__AdminUsername=youradmin
Bootstrap__AdminPassword=a password you choose
```

**Three secrets.** Generate them; nothing generates them for you:

```bash
echo "POSTGRES_PASSWORD=$(openssl rand -base64 24)"
echo "WORKFLOW_CALLBACK_SECRET=$(openssl rand -hex 32)"
echo "YJS_SHARED_SECRET=$(openssl rand -hex 32)"
```

## 3. Start it

```bash
docker compose up -d
```

First start pulls several images and initialises the database; a few minutes is
normal. Watch it settle with `docker compose ps`.

Then open <http://localhost:5108> and sign in with the credentials from step 2.

## Verifying what you downloaded

Every image is pinned by digest, and each carries a signed provenance
attestation tying it to the commit and workflow that built it:

```bash
gh attestation verify \
  oci://ghcr.io/nathanpond/auton8/autonate-web@<digest from compose.yml> \
  --repo nathanpond/Auton8
```

## Upgrading

**Auton8 1.0 requires a fresh database.** Upgrading a 0.x install is not
supported. Releases after 1.0 will carry upgrade paths.

## Stopping and removing

```bash
docker compose down            # stop; data is kept in named volumes
docker compose down --volumes  # stop and DELETE all data
```

## Before anyone else can reach it

This stack terminates no TLS and its services trust each other on the compose
network. `APP_ENVIRONMENT=Development` also keeps a permissive `AllowedHosts`
and relaxes startup checks that exist for good reasons.

For anything beyond a laptop, set `APP_ENVIRONMENT=Production` and
`ALLOWED_HOSTS` to the hostnames you serve, put a reverse proxy in front that
handles HTTPS, and read
[docs/DEPLOYMENT.md](https://github.com/nathanpond/Auton8/blob/master/docs/DEPLOYMENT.md)
— several defaults that are convenient locally are wrong once the app is
reachable from outside the host.
