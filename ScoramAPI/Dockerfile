# syntax=docker/dockerfile:1
#
# Build context is expected to be the ScoramAPI project folder itself, e.g.:
#   docker build -t scoram-api -f Dockerfile .          (run from inside ScoramAPI/)
#   docker build -t scoram-api -f ScoramAPI/Dockerfile ScoramAPI   (run from the SCORAM root)
#
# This does NOT bake in any secret (SQL connection string, JWT key, Meilisearch key, Azure Blob
# connection string, VAPID keys, ...). All of those are read from environment variables at
# container start via ASP.NET Core's standard configuration system -- see appsettings.json /
# Program.cs for the exact keys, and DEPLOYMENT.md for the Render env-var names.

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, using only the csproj, so this layer is cached across builds unless dependencies
# actually change (source-only edits then skip `dotnet restore` entirely on rebuild).
COPY ScoramAPI.csproj ./
RUN dotnet restore ScoramAPI.csproj

# Now copy the rest of the source and publish. .dockerignore keeps bin/, obj/, logs/, local
# uploads, and Meilisearch's local data/executable out of the build context.
COPY . .
RUN dotnet publish ScoramAPI.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Run as a non-root user rather than the image's default root.
RUN useradd --uid 1654 --create-home --shell /bin/false appuser \
    && mkdir -p /app/wwwroot/uploads \
    && chown -R appuser:appuser /app

COPY --from=build --chown=appuser:appuser /app/publish .

USER appuser

# Render (and most host-based PaaS providers) route traffic to whatever port the container listens
# on -- 8080 here, matched by EXPOSE below. Program.cs also honors a PORT env var directly if one is
# supplied instead, but ASPNETCORE_URLS is the primary mechanism and is set unconditionally here.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "ScoramAPI.dll"]
