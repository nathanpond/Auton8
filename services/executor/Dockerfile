# Multi-stage build mirroring services/hocuspocus/Dockerfile. The runtime
# stage carries python3 + make + g++ because isolated-vm needs a native
# build step at install time even in the runtime image (when npm rebuilds
# the prebuilt artifacts for the local arch).
FROM node:24-alpine AS build

WORKDIR /src

# Native build deps for isolated-vm.
RUN apk add --no-cache python3 make g++ linux-headers

COPY package.json package-lock.json* ./
# --ignore-scripts skips every package's install hooks (fast, hermetic) — but
# isolated-vm is a native addon whose install script IS the node-gyp build,
# so without an explicit rebuild the runtime image has no out/isolated_vm.node
# and crashes on boot with "Cannot find module './out/isolated_vm'". Build it
# once here against this image's Node/V8; the runtime stage copies the result.
# npm >= 11.19 also refuses to run install scripts that aren't approved, hence
# the "allowScripts" entry for isolated-vm in package.json. Verify by loading
# the addon rather than by artifact path (5.x built into out/, 7.x into
# build/Release/).
RUN npm install --omit=dev --no-audit --no-fund --ignore-scripts \
 && npm rebuild isolated-vm --foreground-scripts --no-audit --no-fund \
 && node -e "require('isolated-vm')" \
 && cp -R node_modules node_modules_prod \
 && npm install --no-audit --no-fund --ignore-scripts

COPY tsconfig.json ./
COPY src ./src
RUN npx tsc

FROM node:24-alpine AS runtime

WORKDIR /app

# No compiler needed here — isolated-vm was built in the build stage and is
# copied in with node_modules_prod. Keep python3 in case Pyodide loads any
# native pieces lazily.
RUN apk add --no-cache python3

COPY --from=build /src/node_modules_prod ./node_modules
COPY --from=build /src/dist ./dist
COPY package.json ./

ENV NODE_ENV=production

CMD ["node", "dist/index.js"]
