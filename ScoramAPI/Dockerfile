# syntax=docker/dockerfile:1
#
# Build context is expected to be the ScoramAPI project folder itself.

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore dependencies
COPY ScoramAPI.csproj ./
RUN dotnet restore ScoramAPI.csproj

# Copy source and publish
COPY . .
RUN dotnet publish ScoramAPI.csproj -c Release -o /app/publish /p:UseAppHost=false


# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# ASP.NET 8 image already provides the non-root appuser.
# Create required upload directory and give ownership to appuser.
RUN mkdir -p /app/wwwroot/uploads \
    && chown -R appuser:appuser /app

# Copy published application
COPY --from=build --chown=appuser:appuser /app/publish .

# Run as non-root user
USER appuser

# Render will route traffic to port 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "ScoramAPI.dll"]