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

.PHONY: infra-prepare infra-ensure infra-up infra-up-dashboard infra-down infra-reset infra-logs infra-ps app app-dapr rider-sidecar rider-sidecar-status rider-sidecar-stop rider-sidecar-restart

infra-prepare:
	mkdir -p $(POSTGRES_MOUNT) $(REDIS_MOUNT) $(NATS_MOUNT) $(SCHEDULER_MOUNT) $(DAPR_DASHBOARD_COMPONENTS) $(FLOWABLE_DAPR_COMPONENTS) $(MOUNT_ROOT)/flowable $(MOUNT_ROOT)/dapr-placement
	cp ./infra/dapr/components/*.yaml $(DAPR_DASHBOARD_COMPONENTS)/
	# Rewrite the flowable-dapr pubsub copy to use host.docker.internal.
	# Idempotent because we always start from the source file and stream
	# into the destination — sed -i.bak is fragile (silently no-ops on a
	# second run) and leaves a backup file behind.
	sed 's|nats://localhost:4222|nats://host.docker.internal:4222|' ./infra/dapr/components/pubsub.yaml > $(FLOWABLE_DAPR_COMPONENTS)/pubsub.yaml
	./infra/ensure-nats-stream.sh

infra-ensure:
	./infra/ensure-up.sh

rider-sidecar: infra-ensure
	./infra/start-autonate-web-sidecar.sh

rider-sidecar-status:
	./infra/check-autonate-web-sidecar.sh

rider-sidecar-stop:
	./infra/stop-autonate-web-sidecar.sh

rider-sidecar-restart: infra-ensure
	./infra/restart-autonate-web-sidecar.sh

infra-up: infra-prepare
	$(COMPOSE) up -d

infra-up-dashboard: infra-prepare
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
