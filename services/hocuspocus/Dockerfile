# Multi-stage build. Mirrors infra/flowable/Dockerfile's pattern: a build
# stage with the toolchain, a runtime stage with only the artifacts.
# node:24-alpine
FROM node@sha256:e961046fec20896e8904f2b4a8b4c7e5ca91826d84d8d33d83dbaa61f942069e AS build

WORKDIR /src

COPY package.json package-lock.json* ./
RUN npm install --omit=dev --no-audit --no-fund --ignore-scripts \
 && cp -R node_modules node_modules_prod \
 && npm install --no-audit --no-fund --ignore-scripts

COPY tsconfig.json ./
COPY src ./src
RUN npx tsc

# node:24-alpine
FROM node@sha256:e961046fec20896e8904f2b4a8b4c7e5ca91826d84d8d33d83dbaa61f942069e AS runtime

WORKDIR /app

COPY --from=build /src/node_modules_prod ./node_modules
COPY --from=build /src/dist ./dist
COPY package.json ./

ENV NODE_ENV=production
EXPOSE 1234

CMD ["node", "dist/index.js"]
