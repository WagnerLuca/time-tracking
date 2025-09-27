# Build stage - Build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS builder

# Set the working directory
WORKDIR /app

# Copy project file
COPY *.csproj ./

# Restore dependencies
RUN dotnet restore

# Copy the rest of the source code
COPY . ./

# Build the application in Release mode
RUN dotnet publish -c Release -o out

# Runtime stage - Lightweight runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Install curl for health checks (optional)
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Set the working directory
WORKDIR /app

# Copy the published application from builder stage
COPY --from=builder /app/out .

# Create a non-root user for security
RUN adduser --disabled-password --gecos '' --shell /bin/bash appuser && chown -R appuser /app
USER appuser

# Expose the port for the API
EXPOSE 7000

# Start the application
ENTRYPOINT ["dotnet", "TimeTracking.Api.dll"]