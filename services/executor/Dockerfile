# Multi-stage build mirroring services/hocuspocus/Dockerfile. The runtime
# stage carries python3 + make + g++ because isolated-vm needs a native
# build step at install time even in the runtime image (when npm rebuilds
# the prebuilt artifacts for the local arch).
FROM node:22-alpine AS build

WORKDIR /src

# Native build deps for isolated-vm.
RUN apk add --no-cache python3 make g++ linux-headers

COPY package.json package-lock.json* ./
RUN npm install --omit=dev --no-audit --no-fund --ignore-scripts \
 && cp -R node_modules node_modules_prod \
 && npm install --no-audit --no-fund --ignore-scripts

COPY tsconfig.json ./
COPY src ./src
RUN npx tsc

FROM node:22-alpine AS runtime

WORKDIR /app

# isolated-vm prebuilds match the alpine target, but keep the python3
# runtime in case Pyodide loads any native pieces lazily.
RUN apk add --no-cache python3

COPY --from=build /src/node_modules_prod ./node_modules
COPY --from=build /src/dist ./dist
COPY package.json ./

ENV NODE_ENV=production

CMD ["node", "dist/index.js"]
