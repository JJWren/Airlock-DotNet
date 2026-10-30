# The CI tools' images, each pinned by tag and digest (DR-27, U6-NFR-01, U6-NFR-15). This file is never built:
# scripts/ci/run-tool.sh reads each stage's image from it, and Dependabot keeps them current, after its 7-day cooldown.
# Syft's image is the Dockerfile's own syft stage, so the image and its SBOM use the same Syft.
FROM ghcr.io/anchore/grype:v0.121.0@sha256:621d56d1d68106b787022c81546f7e0a36202a25d1239f0c20195fab8c16f7ea AS grype
FROM rhysd/actionlint:1.7.12@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667 AS actionlint
FROM ghcr.io/zizmorcore/zizmor:1.31.1@sha256:42d409dbb851d4c5d35a058cf042b828bea1e3f2884378c526eb0974780e18d9 AS zizmor
FROM moby/buildkit:v0.34.1@sha256:99368f3e46177c6405b9740e4461d91cee004128daeecca78d4ceac50ec1da5c AS buildkit
