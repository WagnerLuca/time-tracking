# Multi-stage build for SvelteKit with SSR support
# Use BUILDPLATFORM for build stage to avoid QEMU issues with npm
FROM --platform=$BUILDPLATFORM node:20-alpine AS builder

# Set the working directory
WORKDIR /app

# Copy package files first (for better layer caching)
COPY package*.json ./

# Install all dependencies (including @sveltejs/adapter-node)
RUN npm ci

# Copy source code
COPY . ./

# Sync SvelteKit to generate .svelte-kit directory and tsconfig and check
RUN npm run check

# Build the SvelteKit application with adapter-node
RUN npm run build

# Production runtime stage - uses target platform
FROM node:20-alpine AS runner

# Install dumb-init for proper signal handling
RUN apk add --no-cache dumb-init

# Create app directory
WORKDIR /app

# Create a non-root user for security
RUN addgroup -g 1001 -S nodejs
RUN adduser -S sveltekit -u 1001

# Copy the built SvelteKit server from builder stage
COPY --from=builder /app/build ./build
COPY --from=builder /app/package.json ./package.json
COPY --from=builder /app/package-lock.json ./package-lock.json

# Install only production dependencies
RUN npm ci --omit=dev && npm cache clean --force

# Change ownership to non-root user
RUN chown -R sveltekit:nodejs /app
USER sveltekit

# Expose the port
EXPOSE 3000

# Start the SvelteKit server
CMD ["dumb-init", "node", "build"]