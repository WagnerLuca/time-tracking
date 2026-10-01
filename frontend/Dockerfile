# syntax=docker/dockerfile:1
# Multi-stage build for SvelteKit with SSR support
# Use BUILDPLATFORM for build stage to avoid QEMU issues with npm
FROM --platform=$BUILDPLATFORM node:22-alpine AS builder

# Set the working directory
WORKDIR /app

# Copy package files first (for better layer caching)
COPY package*.json .npmrc ./

# Install all dependencies (including @sveltejs/adapter-node). @wagnerluca/ui
# comes from GitHub Packages (see .npmrc), which needs a read:packages token,
# passed as a BuildKit secret so it never lands in an image layer.
RUN --mount=type=secret,id=NODE_AUTH_TOKEN     if [ ! -s /run/secrets/NODE_AUTH_TOKEN ]; then       echo "ERROR: NODE_AUTH_TOKEN secret not provided to the build (needed to install @wagnerluca/ui from GitHub Packages)" >&2;       exit 1;     fi &&     NODE_AUTH_TOKEN="$(cat /run/secrets/NODE_AUTH_TOKEN)" npm ci

# Copy source code
COPY . ./

# Sync SvelteKit to generate .svelte-kit directory and tsconfig and check
RUN npm run check

# Build the SvelteKit application with adapter-node
RUN npm run build

# Production runtime stage - uses target platform
FROM node:22-alpine AS runner

# Install dumb-init for proper signal handling
RUN apk add --no-cache dumb-init

WORKDIR /app

# The server bundle in build/ still imports the runtime dependencies (axios, ...),
# so install those; @wagnerluca/ui and the rest of the toolchain are devDependencies.
# Files stay root-owned: the app user can read but not modify its own code.
COPY --from=builder /app/package.json /app/package-lock.json ./
RUN npm ci --omit=dev --ignore-scripts && npm cache clean --force
COPY --from=builder /app/build ./build

# Run as the image's built-in non-root user
USER node

EXPOSE 3000

CMD ["dumb-init", "node", "build"]
