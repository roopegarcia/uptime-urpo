# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM node:26.9.0-bookworm-slim AS frontend
WORKDIR /source/frontend
COPY frontend/package*.json ./
RUN npm ci --ignore-scripts
COPY frontend/ ./
# Test dependencies stay in the builder; Vitest configuration is not needed here.
RUN npx tsc --noEmit && npx vite build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403 AS backend
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY backend/UptimeUrpo.Api/ backend/UptimeUrpo.Api/
COPY --from=frontend /source/frontend/dist/ frontend/dist/
# Framework-dependent IL runs on either runtime architecture; no RID-specific apphost.
RUN dotnet restore backend/UptimeUrpo.Api --locked-mode
RUN dotnet publish backend/UptimeUrpo.Api -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
COPY --from=backend --chown=app:app /out/ ./
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "UptimeUrpo.Api.dll", "--health-check"]
ENTRYPOINT ["dotnet", "UptimeUrpo.Api.dll"]
