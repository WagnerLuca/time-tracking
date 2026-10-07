# syntax=docker/dockerfile:1
# Multi-stage build for SvelteKit with SSR support
# Use BUILDPLATFORM for build stage to avoid QEMU issues with npm
FROM --platform=$BUILDPLATFORM node:26-alpine AS builder

# Set the working directory
WORKDIR /app

# Copy package files first (for better layer caching)
COPY package*.json .npmrc ./

# Install all dependencies (including @sveltejs/adapter-node). @wagnerluca/ui is an
# optional dependency from GitHub Packages (see .npmrc), which needs a read:packages
# token, passed as a BuildKit secret so it never lands in an image layer. Without the
# secret npm skips it and the build uses the bundled fallback tokens (vite.config.ts).
RUN --mount=type=secret,id=NODE_AUTH_TOKEN \
    if [ -s /run/secrets/NODE_AUTH_TOKEN ]; then \
      NODE_AUTH_TOKEN="$(cat /run/secrets/NODE_AUTH_TOKEN)" npm ci; \
    else \
      echo "NODE_AUTH_TOKEN not provided: building with the bundled Wagner Luca design tokens"; \
      npm ci; \
    fi

# Copy source code
COPY . ./

# Sync SvelteKit to generate .svelte-kit directory and tsconfig and check
RUN npm run check

# Build the SvelteKit application with adapter-node
RUN npm run build

# Production runtime stage - uses target platform
FROM node:26-alpine AS runner

# Install dumb-init for proper signal handling
RUN apk add --no-cache dumb-init

WORKDIR /app

# The server bundle in build/ still imports the runtime dependencies (axios, ...),
# so install those; @wagnerluca/ui (optional, CSS only) and the toolchain are not needed.
# Files stay root-owned: the app user can read but not modify its own code.
COPY --from=builder /app/package.json /app/package-lock.json ./
RUN npm ci --omit=dev --omit=optional --ignore-scripts && npm cache clean --force
COPY --from=builder /app/build ./build

# Run as the image's built-in non-root user
USER node

EXPOSE 3000

CMD ["dumb-init", "node", "build"]
