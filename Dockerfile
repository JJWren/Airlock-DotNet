# Airlock's one image (DR-02 to DR-05, DR-10, DR-24). Every base image is pinned by tag and digest (DR-27,
# U6-NFR-01), and the runtime stage runs nothing: the chiseled image has no shell (U6-NFR-08).

# Syft v1.55.0, Anchore's own image; its version is also in the install scripts and the README (DR-04).
FROM ghcr.io/anchore/syft:v1.55.0@sha256:4cc92b64ca74ffca4c94d4c8fcc01f217731375300be14d0b6eeb2e82eaa110a AS syft

# The SDK that global.json names (Q-U6-I1).
FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Airlock.Domain/Airlock.Domain.csproj src/Airlock.Domain/
COPY src/Airlock.Application/Airlock.Application.csproj src/Airlock.Application/
COPY src/Airlock.Infrastructure/Airlock.Infrastructure.csproj src/Airlock.Infrastructure/
COPY src/Airlock.Web/Airlock.Web.csproj src/Airlock.Web/
RUN dotnet restore src/Airlock.Web/Airlock.Web.csproj --runtime linux-x64
COPY src/ src/
RUN dotnet publish src/Airlock.Web/Airlock.Web.csproj --no-restore --configuration Release --runtime linux-x64 \
        --self-contained false -p:UseAppHost=false --output /app \
    && mkdir -p /rootfs/var/lib/airlock/staging /rootfs/var/lib/airlock/keys \
    && chmod 0700 /rootfs/var/lib/airlock/staging /rootfs/var/lib/airlock/keys
COPY LICENSE THIRD-PARTY-NOTICES.md /app/
COPY licenses/ /app/licenses/
# Whatever modes the build context gave its files, only their owner, root, may write anything in /app (DR-03).
RUN chmod -R u=rwX,go=rX /app

# Chiseled Ubuntu 24.04 with ICU and time-zone data, no shell and no package manager (Q-U6-N1).
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra@sha256:9b755ea799ae649141a073e30d8b62d8095a5465af2b7694d17866e8c05131d8 AS runtime
ARG VERSION
ARG REVISION
ARG CREATED
LABEL org.opencontainers.image.source="https://github.com/JJWren/Airlock-DotNet" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.title="Airlock" \
      org.opencontainers.image.description="Software composition analysis for third-party software: an SBOM from Syft, its packages checked against NVD" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.created="${CREATED}"
WORKDIR /app

# The application, its notices and Syft belong to root, so the user that runs them cannot change them (DR-03, DR-05).
COPY --from=build /app/ /app/
COPY --from=syft /syft /app/bin/syft

# The staging root and the data-protection keys, the only folders the user writes, on the volume's mount (DR-03,
# U6-NFR-09, U6-NFR-10).
COPY --from=build --chown=1654:1654 /rootfs/var/lib/airlock/ /var/lib/airlock/

# URLS outranks appsettings.json, where ASPNETCORE_URLS would not; the base image's HTTP_PORTS is cleared so that no
# start warns that URLS overrides it (DR-02).
ENV URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS= \
    Airlock__Staging__Root=/var/lib/airlock/staging \
    Airlock__DataProtection__KeysPath=/var/lib/airlock/keys
USER 1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --start-interval=2s --retries=3 \
    CMD ["/usr/bin/dotnet", "/app/Airlock.Web.dll", "--health-probe"]
ENTRYPOINT ["/usr/bin/dotnet", "/app/Airlock.Web.dll"]
