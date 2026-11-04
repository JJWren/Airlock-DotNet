#!/bin/sh
# Installs Syft v1.55.0 into the repository's bin folder (IR-25).
#
# Downloads the release for this machine's system and architecture from Syft's GitHub releases over HTTPS, compares its
# SHA-256 checksum with the one this script carries and with the release's checksums file before anything is
# extracted, and moves the binary into bin/. Does nothing when bin/ already holds v1.55.0, and stops with a message on
# any failure. Needs curl, and sha256sum or shasum.
set -eu

version=1.55.0
releases=https://github.com/anchore/syft/releases/download
root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
bin="$root/bin"

fail() {
  echo "install-tools: $*" >&2
  exit 1
}

case "$(uname -s)" in
  Linux) os=linux ;;
  Darwin) os=darwin ;;
  *) fail "unsupported system: $(uname -s); on Windows, run install-tools.ps1" ;;
esac

case "$(uname -m)" in
  x86_64 | amd64) arch=amd64 ;;
  aarch64 | arm64) arch=arm64 ;;
  *) fail "unsupported architecture: $(uname -m)" ;;
esac

# Nothing to do when bin/ already holds this version.
if [ -x "$bin/syft" ] && "$bin/syft" version 2>/dev/null | grep -Eq "^Version:[[:space:]]+$version\$"; then
  echo "Syft $version is already in $bin"
  exit 0
fi

command -v curl >/dev/null 2>&1 || fail "curl is required"
if command -v sha256sum >/dev/null 2>&1; then
  digest() { sha256sum "$1" | cut -d ' ' -f 1; }
elif command -v shasum >/dev/null 2>&1; then
  digest() { shasum -a 256 "$1" | cut -d ' ' -f 1; }
else
  fail "sha256sum or shasum is required"
fi

archive="syft_${version}_${os}_${arch}.tar.gz"
checksums="syft_${version}_checksums.txt"

# The SHA-256 of each archive this script can fetch, from Syft v1.55.0's checksums file. A download must match it as
# well as the release's own checksums file, so a replaced release asset stops the script (DR-04, U6-NFR-01).
case "$archive" in
  syft_1.55.0_darwin_amd64.tar.gz) pinned=e2e5831cbabaec9a36fe65c34755c1113982c842a685edce3ce0b54a0ded8003 ;;
  syft_1.55.0_darwin_arm64.tar.gz) pinned=1217c258bd5eb91ef3189cf22a82e02f9cac07e4ce934350f428e93aff674625 ;;
  syft_1.55.0_linux_amd64.tar.gz) pinned=bb65249542db5fedeeb4d3f9b018b75e7264dbfbbb10486f044581a92f90490d ;;
  syft_1.55.0_linux_arm64.tar.gz) pinned=929bd163db2d7cd2255d3bcb8967208079813d7b46b0c952f7b794d8e2c7d304 ;;
  *) fail "this script carries no checksum for $archive" ;;
esac

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT INT TERM

download() {
  curl --fail --silent --show-error --location --proto '=https' --tlsv1.2 -o "$work/$1" "$releases/v$version/$1" \
    || fail "could not download $1"
}

download "$archive"
download "$checksums"

# The checksums are compared before anything is extracted.
actual=$(digest "$work/$archive")
[ "$actual" = "$pinned" ] || fail "the checksum of $archive does not match the one this script carries"
expected=$(awk -v name="$archive" '$2 == name || $2 == "*" name { print $1 }' "$work/$checksums")
[ -n "$expected" ] || fail "$checksums does not list $archive"
[ "$actual" = "$expected" ] || fail "the checksum of $archive does not match the release's"

mkdir -p "$work/extracted"
tar -xzf "$work/$archive" -C "$work/extracted" syft || fail "could not extract syft from $archive"
mkdir -p "$bin"
mv -f "$work/extracted/syft" "$bin/syft"
chmod +x "$bin/syft"
echo "Installed Syft $version in $bin"
