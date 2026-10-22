# Airlock

Airlock is a gateway that checks third-party software before it is admitted into a controlled environment. This
glossary fixes the words used for its concepts in code, the API and the docs.

## Language

### What Airlock does

**SCA (Software Composition Analysis)**:
Identifying the third-party packages inside software and checking them against known-vulnerability records.
_Avoid_: SAST (analysis of first-party source code for insecure patterns, which Airlock does not do)

**Scan**:
One run of Airlock against a scan target: build its SBOM, correlate the packages with NVD, and produce the result.
_Avoid_: audit, security audit, vetting sequence (acceptable only as on-screen copy)

**Active Scan**:
The one scan currently running; Airlock runs at most one scan at a time.

**Scan Status**:
The state of a scan: Running, Completed, Incomplete (finished with at least one unverified package), Failed, or
Cancelled.

**Emergency Stop**:
Cancelling the active scan immediately, whoever started it.
_Avoid_: kill-switch

### What gets scanned

**Scan Target**:
What a scan inspects: a directory path, or a set of uploaded artifacts.

**Artifact**:
Any file submitted for a scan: an archive, package file, container image, binary, or manifest.

**Manifest**:
An artifact that is a text dependency declaration, such as `requirements.txt` or `package-lock.json`.

**SBOM (Software Bill of Materials)**:
The inventory of packages identified inside a scan target.

**Package**:
One third-party component listed in the SBOM, identified by its type, name and version.
_Avoid_: artifact (Syft's word for it), component

### Vulnerability intelligence

**NVD (National Vulnerability Database)**:
NIST's public catalogue of CVE records; Airlock's only vulnerability source.

**CVE**:
A published vulnerability record, identified like `CVE-2020-1747`.

**CPE**:
A standardized product identifier; NVD links each CVE to the CPEs it affects.

**Candidate CPE**:
A CPE proposed for a package, taken from the official CPE dictionary or guessed by Syft.
_Avoid_: CPE guess

**Severity Threshold**:
The lowest severity Airlock reports: HIGH.

### Results

**Correlation**:
Matching each package in the SBOM to NVD records to produce its findings.
_Avoid_: vetting, enrichment

**Match Method**:
How a package was linked to NVD records: a CPE match (through one of its candidate CPEs), or a product match (its
name and version matched against any vendor's product).
_Avoid_: keyword match (the original's description search, retired)

**Finding**:
One CVE, at or above the severity threshold, matched to one package.
_Avoid_: vulnerability report

**Vulnerable Package**:
A package with at least one finding.

**Clean Package**:
A package whose correlation completed and produced no findings.

**Unverified Package**:
A package whose correlation could not be completed — its lookups failed or its version is unknown; never counted as
clean.

**Scan Report**:
The exported result of a scan, as PDF or JSON.
_Avoid_: audit report (acceptable only as on-screen copy)

**Live Log**:
The real-time feed of Airlock's activity messages.
_Avoid_: telemetry
