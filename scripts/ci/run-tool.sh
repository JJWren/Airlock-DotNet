#!/usr/bin/env bash
# Runs a CI tool from its image, pinned by tag and digest (DR-27, DR-28, U6-NFR-01, U6-NFR-15).
#
#   run-tool.sh <tool> [arguments...]   actionlint, shellcheck, zizmor, syft or grype
#   run-tool.sh --ref <stage>           prints a stage's image: actionlint, zizmor, grype, buildkit or syft
#
# The images are read from .github/ci-tools/Dockerfile, and Syft's from the Dockerfile, so each pin has one place, and
# Dependabot keeps it current. The repository is mounted read-only on /repo, the tool's working folder. Syft and Grype
# also get the work folder, $AIRLOCK_CI_WORK, on /work, and run as the calling user. Every tool runs with no network
# except zizmor, whose online audits ask GitHub, and Grype, which fetches its database.
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
work=${AIRLOCK_CI_WORK:-${RUNNER_TEMP:-/tmp}/airlock-ci}

fail() {
  echo "run-tool: $*" >&2
  exit 2
}

# A stage's image: the second word of its FROM line.
ref() {
  local file=$root/.github/ci-tools/Dockerfile
  if [ "$1" = syft ]; then
    file=$root/Dockerfile
  fi

  awk -v stage="$1" 'toupper($1) == "FROM" && tolower($3) == "as" && $4 == stage { print $2; found = 1 } END { exit !found }' \
    "$file" || fail "no stage named $1 in $file"
}

[ $# -ge 1 ] || fail "usage: run-tool.sh <tool> [arguments...] | --ref <stage>"
tool=$1
shift
if [ "$tool" = --ref ]; then
  [ $# -eq 1 ] || fail "usage: run-tool.sh --ref <stage>"
  ref "$1"
  exit 0
fi

options=(--rm --volume "$root:/repo:ro" --workdir /repo)
writes() {
  mkdir -p "$work/tmp"
  options+=(--user "$(id -u):$(id -g)" --volume "$work:/work" --env TMPDIR=/work/tmp)
}

case "$tool" in
  actionlint)
    image=$(ref actionlint)
    options+=(--network none)
    ;;
  shellcheck)
    image=$(ref actionlint)
    options+=(--network none --entrypoint /usr/local/bin/shellcheck)
    ;;
  zizmor)
    # GH_TOKEN is passed by name, so its value never appears on a command line; without it zizmor stays offline.
    image=$(ref zizmor)
    options+=(--env GH_TOKEN)
    ;;
  syft)
    image=$(ref syft)
    writes
    options+=(--network none --env SYFT_CHECK_FOR_APP_UPDATE=false)
    ;;
  grype)
    image=$(ref grype)
    writes
    mkdir -p "$work/grype-db"
    options+=(--env GRYPE_DB_CACHE_DIR=/work/grype-db --env GRYPE_CHECK_FOR_APP_UPDATE=false)
    ;;
  *)
    fail "unknown tool $tool: actionlint, shellcheck, zizmor, syft or grype"
    ;;
esac

exec docker run "${options[@]}" "$image" "$@"
