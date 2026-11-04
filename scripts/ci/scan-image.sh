#!/usr/bin/env bash
# The image scan (DR-30, U6-NFR-06). One Syft run over the OCI archive that build-image.sh wrote gives both SBOMs:
# Syft's own JSON, which keeps the distribution's details for Grype, and SPDX JSON for the attestation. Grype then
# applies .grype.yaml to the first: a High or Critical vulnerability with a fix fails. It writes sbom.syft.json,
# sbom.spdx.json and grype.json to $AIRLOCK_CI_WORK, and exits with Grype's verdict.
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
work=${AIRLOCK_CI_WORK:-${RUNNER_TEMP:-/tmp}/airlock-ci}
export AIRLOCK_CI_WORK=$work

if [ ! -s "$work/image.oci.tar" ]; then
  echo "scan-image: no image.oci.tar in $work: run build-image.sh load first" >&2
  exit 2
fi

rm -f "$work/sbom.syft.json" "$work/sbom.spdx.json" "$work/grype.json"
bash "$root/scripts/ci/run-tool.sh" syft oci-archive:/work/image.oci.tar --quiet \
  --output syft-json=/work/sbom.syft.json --output spdx-json=/work/sbom.spdx.json
bash "$root/scripts/ci/run-tool.sh" grype sbom:/work/sbom.syft.json --output table --output json=/work/grype.json
