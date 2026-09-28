# Multi-stage build. Mirrors infra/flowable/Dockerfile's pattern: a build
# stage with the toolchain, a runtime stage with only the artifacts.
# node:24-alpine
FROM node@sha256:a723b54c35a76e947095a20a67d39585bb09c862e6b1adeb8a9f518f95e34fb0 AS build

WORKDIR /src

COPY package.json package-lock.json* ./
RUN npm install --omit=dev --no-audit --no-fund --ignore-scripts \
 && cp -R node_modules node_modules_prod \
 && npm install --no-audit --no-fund --ignore-scripts

COPY tsconfig.json ./
COPY src ./src
RUN npx tsc

# node:24-alpine
FROM node@sha256:a723b54c35a76e947095a20a67d39585bb09c862e6b1adeb8a9f518f95e34fb0 AS runtime

WORKDIR /app

COPY --from=build /src/node_modules_prod ./node_modules
COPY --from=build /src/dist ./dist
COPY package.json ./

ENV NODE_ENV=production
EXPOSE 1234

CMD ["node", "dist/index.js"]
