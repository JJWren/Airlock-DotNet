#!/usr/bin/env bash
# The release's checks before anything is built (DR-19 to DR-21): the pushed tag is v and a strict semantic version,
# equal to Version in Directory.Build.props, and that version is not yet in the registry. Writes version and latest to
# $GITHUB_OUTPUT when it is set.
#
#   check-release.sh [tag]   the tag defaults to $GITHUB_REF_NAME; on ghcr.io, GITHUB_TOKEN reads the registry
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
registry=${AIRLOCK_REGISTRY:-ghcr.io}
repository=${AIRLOCK_REGISTRY_REPOSITORY:-jjwren/airlock-dotnet}
scheme=${AIRLOCK_REGISTRY_SCHEME:-https}

fail() {
  echo "check-release: $*" >&2
  exit 1
}

tag=${1:-${GITHUB_REF_NAME:-}}
[ -n "$tag" ] || fail "usage: check-release.sh <tag>, or GITHUB_REF_NAME"

# Semantic Versioning 2.0.0, without build metadata, which an image tag cannot carry.
identifier='(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
semver="^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-$identifier(\.$identifier)*)?\$"
[[ $tag == v* ]] || fail "the tag $tag does not start with v"
version=${tag#v}
[[ $version =~ $semver ]] || fail "the tag $tag is not v and a semantic version, such as v2.0.0 or v2.1.0-rc.1"
[ ${#version} -le 128 ] || fail "the version in $tag is longer than an image tag may be"

# DR-20: the version that health and the UI show is the image's.
props=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
[ "$version" = "$props" ] || fail "the tag $tag says $version, but Directory.Build.props says ${props:-nothing}"

# DR-21: a version is published once. GHCR gives no token for a repository without a package, so a first release
# finds none; promote.sh, which the publishing job runs, refuses to move a version tag in any case.
accept='application/vnd.oci.image.manifest.v1+json, application/vnd.oci.image.index.v1+json'
accept="$accept, application/vnd.docker.distribution.manifest.v2+json, application/vnd.docker.distribution.manifest.list.v2+json"
headers="header = \"Accept: $accept\""
published=unknown
if [ "$registry" = ghcr.io ]; then
  # The token goes to curl on its standard input, never on a command line.
  if response=$(printf 'user = "%s:%s"\n' "${GITHUB_ACTOR:-airlock}" "${GITHUB_TOKEN:?GITHUB_TOKEN is needed to read ghcr.io}" \
    | curl --fail --silent --show-error --config - "https://ghcr.io/token?scope=repository:$repository:pull&service=ghcr.io"); then
    headers="$headers
header = \"Authorization: Bearer $(jq -r '.token' <<<"$response")\""
  else
    echo "check-release: $registry has no package $repository that this run may read: taken as no release yet"
    published=no
  fi
fi

if [ "$published" = unknown ]; then
  status=$(printf '%s\n' "$headers" | curl --silent --show-error --head --output /dev/null --write-out '%{http_code}' \
    --config - "$scheme://$registry/v2/$repository/manifests/$version")
  case "$status" in
    404) ;;
    200) fail "$version is already published as $registry/$repository:$version; a fix needs a new version" ;;
    *) fail "$registry answered $status for $repository:$version" ;;
  esac
fi

latest=true
if [[ $version == *-* ]]; then
  latest=false
fi
echo "Releasing $version (latest: $latest)"
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "version=$version"
    echo "latest=$latest"
  } >> "$GITHUB_OUTPUT"
fi
