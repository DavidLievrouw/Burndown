# Build context: the repository root (docker build -f src/Burndown.Web/Dockerfile .)

# Stage 1: the React client. The Vite build writes into src/Burndown.Web/wwwroot (see vite.config.ts).
FROM node:24-bookworm-slim AS client
WORKDIR /repo/src/Burndown.Client
COPY src/Burndown.Client/package.json src/Burndown.Client/package-lock.json ./
RUN npm ci
COPY src/Burndown.Client/ ./
RUN npm run build \
    && test -s /repo/src/Burndown.Web/wwwroot/index.html

# Stage 2: publish the host with the built client as its static assets.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /repo
COPY global.json version.txt .editorconfig ./
COPY src/Directory.Build.props src/Directory.Packages.props src/version.props ./src/
COPY src/Burndown.Common/ ./src/Burndown.Common/
COPY src/Burndown.Firefly/ ./src/Burndown.Firefly/
COPY src/Burndown.Overview/ ./src/Burndown.Overview/
COPY src/Burndown.Entry/ ./src/Burndown.Entry/
COPY src/Burndown.Web/ ./src/Burndown.Web/
COPY --from=client /repo/src/Burndown.Web/wwwroot/ ./src/Burndown.Web/wwwroot/

ARG SOURCE_REVISION_ID=
RUN dotnet restore src/Burndown.Web/Burndown.Web.csproj \
    && dotnet publish src/Burndown.Web/Burndown.Web.csproj -c Release --no-restore -o /out \
    -p:SourceRevisionId=${SOURCE_REVISION_ID} -p:UseAppHost=false \
    && test -s /out/wwwroot/index.html

# Stage 3: chiseled runtime (no shell, non-root). The nl-BE culture and the IANA time zone need the -extra variant.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /out .
# Listen on all container interfaces: exposure is restricted by the compose port mapping (127.0.0.1 only).
ENV ASPNETCORE_URLS=http://+:9042 \
    ASPNETCORE_HTTP_PORTS= \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimitPercent=50 \
    DOTNET_TieredPGO=1
USER $APP_UID
EXPOSE 9042
HEALTHCHECK --interval=30s --timeout=15s --start-period=30s CMD ["dotnet", "Burndown.Web.dll", "--health-check"]
ENTRYPOINT ["dotnet", "Burndown.Web.dll"]
