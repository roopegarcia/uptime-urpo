# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM node:26.9.0-bookworm-slim@sha256:582460f614631b59b824ac6020533b9bf339c7fdf3a6d7db31abb6b4065f0212 AS frontend
WORKDIR /source/frontend
COPY frontend/package*.json ./
RUN npm ci --ignore-scripts
COPY frontend/ ./
# Test dependencies stay in the builder; Vitest configuration is not needed here.
RUN npx tsc --noEmit && npx vite build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS backend
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY backend/UptimeUrpo.Api/ backend/UptimeUrpo.Api/
COPY --from=frontend /source/frontend/dist/ frontend/dist/
# Framework-dependent IL runs on either runtime architecture; no RID-specific apphost.
RUN dotnet restore backend/UptimeUrpo.Api --locked-mode
RUN dotnet publish backend/UptimeUrpo.Api -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS runtime
ARG REVISION=unknown
LABEL org.opencontainers.image.revision=$REVISION
WORKDIR /app
COPY --from=backend --chown=app:app /out/ ./
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "UptimeUrpo.Api.dll", "--health-check"]
ENTRYPOINT ["dotnet", "UptimeUrpo.Api.dll"]
