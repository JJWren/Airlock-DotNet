#!/bin/sh
# Installs Syft v1.55.0 into the repository's bin folder (IR-25).
#
# Downloads the release for this machine's system and architecture from Syft's GitHub releases over HTTPS, compares its
# SHA-256 checksum with the release's checksums file before anything is extracted, and moves the binary into bin/. Does
# nothing when bin/ already holds v1.55.0, and stops with a message on any failure. Needs curl, and sha256sum or shasum.
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
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT INT TERM

download() {
  curl --fail --silent --show-error --location --proto '=https' --tlsv1.2 -o "$work/$1" "$releases/v$version/$1" \
    || fail "could not download $1"
}

download "$archive"
download "$checksums"

# The checksum is compared before anything is extracted.
expected=$(awk -v name="$archive" '$2 == name || $2 == "*" name { print $1 }' "$work/$checksums")
[ -n "$expected" ] || fail "$checksums does not list $archive"
[ "$(digest "$work/$archive")" = "$expected" ] || fail "the checksum of $archive does not match the release's"

mkdir -p "$work/extracted"
tar -xzf "$work/$archive" -C "$work/extracted" syft || fail "could not extract syft from $archive"
mkdir -p "$bin"
mv -f "$work/extracted/syft" "$bin/syft"
chmod +x "$bin/syft"
echo "Installed Syft $version in $bin"
