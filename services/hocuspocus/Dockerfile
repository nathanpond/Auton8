# Multi-stage build. Mirrors infra/flowable/Dockerfile's pattern: a build
# stage with the toolchain, a runtime stage with only the artifacts.
FROM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5 AS build

WORKDIR /src

COPY package.json package-lock.json* ./
RUN npm install --omit=dev --no-audit --no-fund --ignore-scripts \
 && cp -R node_modules node_modules_prod \
 && npm install --no-audit --no-fund --ignore-scripts

COPY tsconfig.json ./
COPY src ./src
RUN npx tsc

FROM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5 AS runtime

WORKDIR /app

COPY --from=build /src/node_modules_prod ./node_modules
COPY --from=build /src/dist ./dist
COPY package.json ./

ENV NODE_ENV=production
EXPOSE 1234

CMD ["node", "dist/index.js"]
