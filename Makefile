COMPOSE := docker compose -f infra/docker-compose.yml
APP_PROJECT := ./src/AutoNate.Web
APP_PROFILE := http
APP_PORT := 5108
DAPR_APP_ID := autonate-web
DAPR_HTTP_PORT := 3500
DAPR_GRPC_PORT := 50001
DAPR_PLACEMENT_HOST_ADDRESS := 127.0.0.1:50006
DAPR_SCHEDULER_HOST_ADDRESS := 127.0.0.1:50007
MOUNT_ROOT := ./infra/mounts
POSTGRES_MOUNT := $(MOUNT_ROOT)/postgres/data
REDIS_MOUNT := $(MOUNT_ROOT)/redis/data
NATS_MOUNT := $(MOUNT_ROOT)/nats/data
SCHEDULER_MOUNT := $(MOUNT_ROOT)/dapr-scheduler/data
DAPR_DASHBOARD_COMPONENTS := $(MOUNT_ROOT)/dapr-dashboard/components
FLOWABLE_DAPR_COMPONENTS := $(MOUNT_ROOT)/flowable-dapr/components

include tests/tiers.env

.PHONY: test-slim test-full-local app-container app-container-down lockfiles preflight infra-prepare infra-ensure infra-up infra-up-dashboard infra-down infra-reset infra-logs infra-ps app app-dapr rider-sidecar rider-sidecar-status rider-sidecar-stop rider-sidecar-restart e2e e2e-install

# Verify the documented prerequisites and port availability before anything
# tries to start. Reports every problem in one pass so a machine is fixed once,
# rather than once per missing tool. Required versions live in
# infra/prerequisites; ports are derived from the compose file.
# Regenerate every packages.lock.json after changing a PackageReference.
# CI restores in locked mode, so a changed reference without a regenerated lock
# file fails the build rather than silently resolving something new.
# The plugin projects are listed separately because plugins/Directory.Build.props
# does not chain to the root one and they are not in the solution (see #120).
lockfiles:
	dotnet restore AutoNate.sln --force-evaluate
	dotnet restore plugins/HelloPlugin --force-evaluate
	dotnet restore plugins/Auditor --force-evaluate

preflight:
	./infra/preflight.sh

infra-prepare:
	mkdir -p $(POSTGRES_MOUNT) $(REDIS_MOUNT) $(NATS_MOUNT) $(SCHEDULER_MOUNT) $(DAPR_DASHBOARD_COMPONENTS) $(FLOWABLE_DAPR_COMPONENTS) $(MOUNT_ROOT)/flowable $(MOUNT_ROOT)/dapr-placement
	cp ./infra/dapr/components/*.yaml $(DAPR_DASHBOARD_COMPONENTS)/
	# Rewrite the flowable-dapr pubsub copy to use host.docker.internal.
	# Idempotent because we always start from the source file and stream
	# into the destination — sed -i.bak is fragile (silently no-ops on a
	# second run) and leaves a backup file behind.
	sed 's|nats://localhost:4222|nats://host.docker.internal:4222|' ./infra/dapr/components/pubsub.yaml > $(FLOWABLE_DAPR_COMPONENTS)/pubsub.yaml
	./infra/ensure-nats-stream.sh

infra-ensure: preflight
	./infra/ensure-up.sh

rider-sidecar: infra-ensure
	./infra/start-autonate-web-sidecar.sh

rider-sidecar-status:
	./infra/check-autonate-web-sidecar.sh

rider-sidecar-stop:
	./infra/stop-autonate-web-sidecar.sh

rider-sidecar-restart: infra-ensure
	./infra/restart-autonate-web-sidecar.sh

infra-up: preflight infra-prepare
	$(COMPOSE) up -d

infra-up-dashboard: infra-prepare
	./infra/preflight.sh --profile dashboard
	$(COMPOSE) --profile dashboard up -d

# ── The `keycloak` profile: a real OIDC + SAML identity provider ────────────
#
# Off by default. Two things have to be true before it is useful, and both fail
# late and confusingly if left to chance, so they are checked here:
#
#   1. Admin credentials exist. There is no working default (invariant 1 holds
#      for development dependencies too), and compose cannot enforce it without
#      breaking `make infra-up` for everyone who never touches Keycloak — a
#      `:?` guard is evaluated at parse time, not at profile start.
#
#   2. `keycloak` resolves to 127.0.0.1 on this machine. The issuer URL is
#      http://keycloak:PORT so that it is identical from the browser, from a
#      host-run Auton8, and from a containerised one; without the hosts entry
#      the browser cannot reach it, and the symptom is an issuer mismatch deep
#      inside an OIDC library rather than a name that does not resolve.
KEYCLOAK_PORT ?= $(shell sed -n 's/^AUTONATE_KEYCLOAK_PORT=//p' .env 2>/dev/null | tail -1)
KEYCLOAK_PORT := $(if $(KEYCLOAK_PORT),$(KEYCLOAK_PORT),8082)

keycloak-check:
	@sh infra/keycloak/check.sh

keycloak-up: infra-prepare keycloak-check
	./infra/preflight.sh --profile keycloak
	$(COMPOSE) --profile keycloak up -d keycloak
	@echo ""
	@echo "Keycloak is starting. Admin console: http://keycloak:$(KEYCLOAK_PORT)/admin/"
	@echo "Realm 'auton8' — OIDC discovery:"
	@echo "  http://keycloak:$(KEYCLOAK_PORT)/realms/auton8/.well-known/openid-configuration"
	@echo "See docs/DEVELOPMENT.md for what to put in Auton8's provider configuration."

keycloak-down:
	$(COMPOSE) --profile keycloak rm -sf keycloak

keycloak-logs:
	$(COMPOSE) --profile keycloak logs -f keycloak

infra-down:
	$(COMPOSE) down

infra-reset:
	$(COMPOSE) down
	# Guard against the variables being empty (which would expand to
	# `rm -rf` with no operand and either no-op or error depending on the
	# shell), and quote each path so a whitespace-bearing MOUNT_ROOT
	# doesn't get word-split into a much wider deletion target.
	@test -n "$(MOUNT_ROOT)" || { echo "MOUNT_ROOT is empty; refusing to rm -rf"; exit 1; }
	@test -n "$(POSTGRES_MOUNT)" || { echo "POSTGRES_MOUNT is empty; refusing to rm -rf"; exit 1; }
	rm -rf "$(POSTGRES_MOUNT)" "$(REDIS_MOUNT)" "$(NATS_MOUNT)" "$(SCHEDULER_MOUNT)" "$(DAPR_DASHBOARD_COMPONENTS)" "$(FLOWABLE_DAPR_COMPONENTS)"
	$(MAKE) infra-prepare

infra-logs:
	$(COMPOSE) logs -f

infra-ps:
	$(COMPOSE) ps

# Run the whole product as containers: Docker is the only prerequisite.
#
# Both compose files are needed. The app services live in the main file behind
# the `app` profile; docker-compose.app.yml rewires flowable's and hocuspocus's
# callbacks to reach the app over the compose network instead of at
# host.docker.internal, which is only correct when the app is a host process.
app-container: preflight infra-prepare
	# The tracked component files address localhost, which is right for the
	# host-run sidecar: the services publish their ports on the host. This
	# sidecar shares the app CONTAINER's network namespace, where localhost is
	# the app itself — so every service address has to become its compose
	# service name. Missing one is not a warning: daprd exits with
	# INIT_COMPONENT_FAILURE and takes the app down with it, because the app
	# refuses to run without a sidecar.
	#
	# Streamed from the source files rather than edited in place, so this is
	# idempotent — `sed -i` silently no-ops on a second run and leaves a .bak
	# behind, the same trap infra-prepare documents.
	mkdir -p $(MOUNT_ROOT)/autonate-web-dapr/components
	sed -e 's|nats://localhost:4222|nats://nats:4222|' -e 's|localhost:6379|redis:6379|' \
		./infra/dapr/components/pubsub.yaml > $(MOUNT_ROOT)/autonate-web-dapr/components/pubsub.yaml
	sed -e 's|nats://localhost:4222|nats://nats:4222|' -e 's|localhost:6379|redis:6379|' \
		./infra/dapr/components/statestore.yaml > $(MOUNT_ROOT)/autonate-web-dapr/components/statestore.yaml
	$(COMPOSE) -f infra/docker-compose.app.yml --profile app up -d --build

# Stop ONLY the app containers. `compose --profile app down` would tear down
# the entire project — every supporting service with it — which is not what
# "stop the app" means to anyone typing this, and cost a full stack restart
# the first time it was used.
app-container-down:
	$(COMPOSE) -f infra/docker-compose.app.yml rm -sf autonate-web autonate-web-dapr

app: app-dapr

app-dapr: infra-ensure
	dapr run \
		--app-id $(DAPR_APP_ID) \
		--app-port $(APP_PORT) \
		--dapr-http-port $(DAPR_HTTP_PORT) \
		--dapr-grpc-port $(DAPR_GRPC_PORT) \
		--placement-host-address $(DAPR_PLACEMENT_HOST_ADDRESS) \
		--scheduler-host-address $(DAPR_SCHEDULER_HOST_ADDRESS) \
		--resources-path $(DAPR_DASHBOARD_COMPONENTS) \
		-- dotnet run --project $(APP_PROJECT) --launch-profile $(APP_PROFILE)

# Playwright E2E suite. The fixture (AutoNateE2EFixture) creates a dedicated
# `AutoNate_E2E` Postgres database each run and replays
# infra/postgres/init/02-create-autonate-app-schema.sql against it, so this
# target needs only the same infra `app` does. We build the test project up
# front so the Playwright install script (which is shipped inside the test
# assembly) can be invoked via `dotnet exec`, then `dotnet test --no-build`
# skips a redundant rebuild.
e2e-install:
	dotnet build tests/AutoNate.E2E.Tests
	# Idempotent: a no-op when the right Chromium build is already on disk.
	dotnet exec \
		--runtimeconfig tests/AutoNate.E2E.Tests/bin/Debug/net10.0/AutoNate.E2E.Tests.runtimeconfig.json \
		--depsfile tests/AutoNate.E2E.Tests/bin/Debug/net10.0/AutoNate.E2E.Tests.deps.json \
		tests/AutoNate.E2E.Tests/bin/Debug/net10.0/Microsoft.Playwright.dll install chromium

# ---- test tiers ------------------------------------------------------------
#
# slim is what GitHub runs, and `make test-slim` runs ALL of it -- not the xUnit
# subset. A developer who runs a partial slim green and then eats a red build
# from lint or the a11y ratchet has been handed a false gate.
test-slim: e2e-install
	@echo "== slim: SPA =="
	cd src/AutoNate.Spa && npm run lint && npx tsc -b && npm test && npm run build
	@echo "== slim: backend =="
	@# The EXACT pin GitHub checks, not just a non-zero count (#476). CLAUDE.md
	@# promises this target runs everything GitHub runs; once the workflow gained
	@# pins, a target without them would make that promise false in the direction
	@# that matters -- green here, red on the PR.
	@discovered=$$(dotnet test tests/AutoNate.Web.Tests --nologo --list-tests 2>/dev/null \
	    | grep -cE '^    [A-Za-z]'); \
	  if [ "$$discovered" != "$(AUTONATE_TIER_COUNT_SLIM_BACKEND)" ]; then \
	    echo "backend discovered $$discovered tests, pinned at $(AUTONATE_TIER_COUNT_SLIM_BACKEND) in tests/tiers.env."; \
	    echo "If that was deliberate, move the pin in the same commit so the change is in the diff."; \
	    exit 1; \
	  fi; \
	  echo "slim backend: $$discovered tests, at its pin"
	dotnet test tests/AutoNate.Web.Tests --nologo
	@echo "== slim: E2E (untraited only) =="
	@# A filter that matches nothing runs no tests and exits 0 -- this repo's own
	@# named failure mode. The pin subsumes it, and says which direction moved.
	@count=$$(dotnet test tests/AutoNate.E2E.Tests --nologo --list-tests \
	    --filter "$(AUTONATE_TIER_SLIM_FILTER)" 2>/dev/null | grep -cE '^    [A-Za-z]'); \
	  if [ "$$count" -eq 0 ]; then \
	    echo "slim discovered ZERO E2E tests -- the filter matched nothing, which reads as a faster, greener build"; \
	    exit 1; \
	  fi; \
	  if [ "$$count" != "$(AUTONATE_TIER_COUNT_SLIM_E2E)" ]; then \
	    echo "slim E2E discovered $$count tests, pinned at $(AUTONATE_TIER_COUNT_SLIM_E2E) in tests/tiers.env."; \
	    echo "A RequiresService trait on a slim class moves a test out of this tier -- that is the shrink the pin exists to show."; \
	    exit 1; \
	  fi; \
	  echo "slim E2E: $$count tests discovered, at its pin"
	dotnet test tests/AutoNate.E2E.Tests --nologo --filter "$(AUTONATE_TIER_SLIM_FILTER)"

# full-local is everything except Keycloak, with real services. #473 gives this
# target its service stand-up and its preflight; until then it runs the tier
# against whatever is already up.
# Dapr is IN this tier -- the owner's split is "everything except keycloak" --
# so the app runs with a sidecar and the RequiresService=Dapr specs are actually
# exercised rather than quietly excluded.
test-full-local: e2e-install
	@# ensure-up runs FIRST, and that is not a regression -- it is what STARTS
	@# the services, so a preflight ahead of it would fail on every cold machine
	@# (#487). The claim that it ran "before the compose-up" was simply false:
	@# `infra-ensure` was a prerequisite, and make builds prerequisites before
	@# the recipe.
	@#
	@# What the preflight adds on the failure path is the NAME. ensure-up waits
	@# 120s and then says "Compose stack did not become ready", which cannot
	@# distinguish "Docker is not running" from "Flowable crashed on boot". So a
	@# failure there hands off to the preflight, which says which service and
	@# which endpoint before the target gives up.
	@$(MAKE) --no-print-directory infra-ensure || { 	  echo ""; 	  echo "== the stack did not come up. which service? =="; 	  ./infra/tier-preflight.sh || true; 	  exit 1; 	}
	@# And again after it is up, because a container can be `healthy` while the
	@# endpoint behind it is dead -- which IS the fast case: it fails in a second.
	./infra/tier-preflight.sh
	@# Every step's status is captured and OR-ed into rc rather than allowed to
	@# abort the recipe, so the integrity check and the summary run even when the
	@# suite is red -- a lost test otherwise hides behind a failure, which is how
	@# backend-reconcile already works and for the same reason.
	@#
	@# `{ cmd; echo $$? > f; } | tee log` rather than a bare pipe: a pipeline's
	@# status is its LAST command's, so `dotnet test | tee` is always tee's 0.
	@# The measured pre-fix behaviour was `Failed: 2, Passed: 340, Skipped: 1`
	@# and `make test-full-local` exiting 0. There is no `set -o pipefail` to
	@# lean on -- make runs recipes under /bin/sh with no SHELL override here.
	@rc=0; \
	  echo "== full-local: backend =="; \
	  { dotnet test tests/AutoNate.Web.Tests --nologo 2>&1; echo $$? > /tmp/n8-full-backend.rc; } \
	    | tee /tmp/n8-full-backend.log; \
	  [ "$$(cat /tmp/n8-full-backend.rc)" -eq 0 ] || rc=1; \
	  echo "== full-local: E2E (all but Keycloak) =="; \
	  { dotnet test tests/AutoNate.E2E.Tests --nologo --filter "$(AUTONATE_TIER_FULL_LOCAL_FILTER)" 2>&1; \
	    echo $$? > /tmp/n8-full-e2e.rc; } | tee /tmp/n8-full-e2e.log; \
	  [ "$$(cat /tmp/n8-full-e2e.rc)" -eq 0 ] || rc=1; \
	  echo ""; \
	  ./infra/tier-integrity.sh /tmp/n8-full-backend.log /tmp/n8-full-e2e.log || rc=1; \
	  echo ""; echo "== full-local summary =="; \
	  echo "  backend : $$(grep -hoE 'Passed: +[0-9]+' /tmp/n8-full-backend.log 2>/dev/null | tail -1)"; \
	  echo "  E2E     : $$(grep -hoE 'Passed: +[0-9]+' /tmp/n8-full-e2e.log 2>/dev/null | tail -1)"; \
	  echo "  skipped : $$(grep -hoE 'Skipped: +[0-9]+' /tmp/n8-full-backend.log /tmp/n8-full-e2e.log 2>/dev/null | tr -s " " | paste -sd" " -)"; \
	  [ $$rc -eq 0 ] && echo "  result  : PASS" || echo "  result  : FAIL"; \
	  exit $$rc

# RETIRED (#472). `make e2e` ran the E2E project UNFILTERED against a stack that
# already has Flowable and Dapr, so it was a fourth, unnamed tier -- and it went
# red rather than skipping on the Keycloak specs. It now points at the named
# tiers rather than surviving as a thing nobody can place.
e2e:
	@echo "make e2e is retired. The tiers are named now:"
	@echo "  make test-slim        what GitHub runs"
	@echo "  make test-full-local  everything except Keycloak, with real services"
	@echo "See CLAUDE.md > Test tiers."
	@exit 1
