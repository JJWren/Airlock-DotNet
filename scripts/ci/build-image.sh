#!/usr/bin/env bash
# Builds Airlock's image in BuildKit, run in a container from the image pinned in .github/ci-tools/Dockerfile (DR-13,
# DR-22, U6-NFR-07).
#
#   build-image.sh load   builds the image into Docker as airlock-dotnet:smoke, for the smoke test, and as an OCI
#                         archive, for the SBOM and the size. Writes image-id: its configuration digest.
#   build-image.sh push   exports the same build again, from the builder's cache, to the registry by digest and with no
#                         tag. Writes image-digest, the manifest digest, once the configuration it names is image-id's.
#
# Both builds use --platform linux/amd64 and no BuildKit attestations, and take SOURCE_DATE_EPOCH from the commit, so
# the image's timestamps and its created label come from the commit. What they write goes to $AIRLOCK_CI_WORK.
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
work=${AIRLOCK_CI_WORK:-${RUNNER_TEMP:-/tmp}/airlock-ci}
builder=${AIRLOCK_BUILDER:-airlock-dotnet}
repository=${AIRLOCK_IMAGE_REPOSITORY:-ghcr.io/jjwren/airlock-dotnet}

fail() {
  echo "build-image: $*" >&2
  exit 1
}

mode=${1:-}
case "$mode" in
  load | push) ;;
  *) fail "usage: build-image.sh load|push" ;;
esac
command -v jq >/dev/null || fail "jq is required"
mkdir -p "$work"

SOURCE_DATE_EPOCH=$(git -C "$root" log -1 --format=%ct)
export SOURCE_DATE_EPOCH
created=$(date -u -d "@$SOURCE_DATE_EPOCH" +%Y-%m-%dT%H:%M:%SZ)
revision=$(git -C "$root" rev-parse HEAD)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
[ -n "$version" ] || fail "Directory.Build.props names no Version"

# BuildKit in a container: its documented way to push an image by digest alone. A rehearsal against a registry on
# this machine also gives it the host's network and a buildkitd.toml that allows plain HTTP to that registry.
if ! docker buildx inspect "$builder" >/dev/null 2>&1; then
  create=(--name "$builder" --driver docker-container --driver-opt "image=$(bash "$root/scripts/ci/run-tool.sh" --ref buildkit)")
  if [ -n "${AIRLOCK_BUILDER_NETWORK:-}" ]; then
    create+=(--driver-opt "network=$AIRLOCK_BUILDER_NETWORK")
  fi
  if [ -n "${AIRLOCK_BUILDKITD_CONFIG:-}" ]; then
    create+=(--buildkitd-config "$AIRLOCK_BUILDKITD_CONFIG")
  fi
  docker buildx create "${create[@]}" >/dev/null
fi
docker buildx inspect --bootstrap "$builder" >/dev/null

build=(docker buildx build --builder "$builder" --platform linux/amd64 --provenance=false --sbom=false
  --build-arg "VERSION=$version" --build-arg "REVISION=$revision" --build-arg "CREATED=$created")

if [ "$mode" = load ]; then
  rm -f "$work/image.oci.tar" "$work/image-id"
  "${build[@]}" --metadata-file "$work/load.metadata.json" \
    --output "type=docker,name=airlock-dotnet:smoke" --output "type=oci,dest=$work/image.oci.tar" "$root"

  # With two outputs the metadata names the manifest only, so the configuration digest is read from that manifest in
  # the archive.
  manifest=$(tar -xOf "$work/image.oci.tar" index.json | jq -r '.manifests[0].digest')
  [ "$manifest" = "$(jq -r '."containerimage.digest" // empty' "$work/load.metadata.json")" ] \
    || fail "the archive's manifest $manifest is not the one the build reported"
  tested=$(tar -xOf "$work/image.oci.tar" "blobs/sha256/${manifest#sha256:}" | jq -r '.config.digest // empty')
  [ -n "$tested" ] || fail "the archive's manifest names no configuration"
  echo "$tested" > "$work/image-id"
  echo "Built airlock-dotnet:smoke, configuration digest $tested"
  exit 0
fi

[ -s "$work/image-id" ] || fail "no image-id in $work: run build-image.sh load first"
tested=$(cat "$work/image-id")
rm -f "$work/image-digest"
"${build[@]}" --metadata-file "$work/push.metadata.json" \
  --output "type=image,name=$repository,push-by-digest=true,name-canonical=true,push=true" "$root"
digest=$(jq -r '."containerimage.digest" // empty' "$work/push.metadata.json")
pushed=$(jq -r '."containerimage.config.digest" // empty' "$work/push.metadata.json")
[ -n "$digest" ] || fail "the push gave no manifest digest"
if [ -n "$pushed" ] && [ "$pushed" != "$tested" ]; then
  fail "the pushed image's configuration digest is $pushed, not the tested $tested"
fi

# The manifest the registry now serves for that digest must name the tested configuration too.
served=$(docker buildx imagetools inspect --builder "$builder" --raw "$repository@$digest" | jq -r '.config.digest')
[ "$served" = "$tested" ] || fail "$repository@$digest names the configuration $served, not the tested $tested"
echo "$digest" > "$work/image-digest"
echo "Pushed $repository@$digest, configuration digest $tested"
