# Multi-stage build. Mirrors infra/flowable/Dockerfile's pattern: a build
# stage with the toolchain, a runtime stage with only the artifacts.
# node:24-alpine
FROM node@sha256:1b84b89ada025e3b14543cbb837ffaa1c1b5273b841c5b94dd1a8f9c1d8cf7fe AS build

WORKDIR /src

COPY package.json package-lock.json* ./
RUN npm install --omit=dev --no-audit --no-fund --ignore-scripts \
 && cp -R node_modules node_modules_prod \
 && npm install --no-audit --no-fund --ignore-scripts

COPY tsconfig.json ./
COPY src ./src
RUN npx tsc

# node:24-alpine
FROM node@sha256:1b84b89ada025e3b14543cbb837ffaa1c1b5273b841c5b94dd1a8f9c1d8cf7fe AS runtime

WORKDIR /app

COPY --from=build /src/node_modules_prod ./node_modules
COPY --from=build /src/dist ./dist
COPY package.json ./

ENV NODE_ENV=production
EXPOSE 1234

CMD ["node", "dist/index.js"]
