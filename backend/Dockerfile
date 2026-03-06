# Build stage - Build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Set the working directory
WORKDIR /app
# Copy the published application from builder stage
COPY --from=builder /app/out .
# Run as built-in non-root user
USER app
# Expose the port for the API
EXPOSE 7000
# Start the application
ENTRYPOINT ["dotnet", "TimeTracking.Api.dll"]
