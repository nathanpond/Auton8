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

.PHONY: app-container app-container-down lockfiles preflight infra-prepare infra-ensure infra-up infra-up-dashboard infra-down infra-reset infra-logs infra-ps app app-dapr rider-sidecar rider-sidecar-status rider-sidecar-stop rider-sidecar-restart e2e e2e-install

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

e2e: infra-ensure e2e-install
	dotnet test tests/AutoNate.E2E.Tests --no-build
