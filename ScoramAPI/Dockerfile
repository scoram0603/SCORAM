# syntax=docker/dockerfile:1

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ScoramAPI.csproj ./
RUN dotnet restore ScoramAPI.csproj

COPY . .

RUN dotnet publish ScoramAPI.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

# Create upload directory.
# .NET 8 image already contains the non-root "app" user.
RUN mkdir -p /app/wwwroot/uploads \
    && chmod -R 777 /app/wwwroot/uploads

COPY --from=build /app/publish .

# Use the existing .NET 8 non-root user.
USER app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "ScoramAPI.dll"]