#!/usr/bin/env bash
# The release's last steps (DR-21, DR-22, DR-31, U6-NFR-07), on the digest that build-image.sh push wrote to
# $AIRLOCK_CI_WORK/image-digest. The image is named by that digest throughout, never by a tag.
#
#   promote.sh verify          verifies both attestations with gh attestation verify, as a user would: the build
#                              provenance, signed by release.yml, and the SBOM.
#   promote.sh tag <version>   tags the digest X.Y.Z, and latest when the version has no pre-release suffix, without
#                              writing a new manifest, then checks that each tag names the digest. A version tag that
#                              already names another image is never moved.
set -euo pipefail

work=${AIRLOCK_CI_WORK:-${RUNNER_TEMP:-/tmp}/airlock-ci}
builder=${AIRLOCK_BUILDER:-airlock-dotnet}
repository=${AIRLOCK_IMAGE_REPOSITORY:-ghcr.io/jjwren/airlock-dotnet}
github=${AIRLOCK_GITHUB_REPOSITORY:-JJWren/Airlock-DotNet}

fail() {
  echo "promote: $*" >&2
  exit 1
}

# The manifest digest a reference names in the registry; fails when it names nothing.
digest_of() {
  docker buildx imagetools inspect --builder "$builder" --format '{{json .Manifest}}' "$1" | jq -r '.digest'
}

mode=${1:-}
[ -s "$work/image-digest" ] || fail "no image-digest in $work: run build-image.sh push first"
digest=$(cat "$work/image-digest")
command -v jq >/dev/null || fail "jq is required"

case "$mode" in
  verify)
    signer="$github/.github/workflows/release.yml"
    gh attestation verify "oci://$repository@$digest" --repo "$github" --signer-workflow "$signer"
    gh attestation verify "oci://$repository@$digest" --repo "$github" --signer-workflow "$signer" \
      --predicate-type https://spdx.dev/Document/v2.3
    ;;
  tag)
    version=${2:-}
    [ -n "$version" ] || fail "usage: promote.sh tag <version>"
    tags=("$version")
    if [[ $version != *-* ]]; then
      tags+=(latest)
    fi

    # DR-21: a published version never changes, whatever the version job could read.
    if existing=$(digest_of "$repository:$version" 2>/dev/null) && [ -n "$existing" ] && [ "$existing" != "$digest" ]; then
      fail "$repository:$version already names $existing; a fix needs a new version"
    fi

    arguments=(--builder "$builder" --prefer-index=false)
    for tag in "${tags[@]}"; do
      arguments+=(--tag "$repository:$tag")
    done
    docker buildx imagetools create "${arguments[@]}" "$repository@$digest"

    for tag in "${tags[@]}"; do
      named=$(digest_of "$repository:$tag")
      [ "$named" = "$digest" ] || fail "$repository:$tag names $named, not $digest"
      echo "$repository:$tag names $digest"
    done
    ;;
  *)
    fail "usage: promote.sh verify | promote.sh tag <version>"
    ;;
esac
