# Unit of Work — Story Map

**Stage**: INCEPTION — Units Generation (Standard depth)

● = main work for the epic in that unit · ○ = contributes. An epic is complete after its last contributing unit,
when all of its Gherkin scenarios pass.

## Epics × units

| Epic | U1 | U2 | U3 | U4 | U5 | U6 | Complete after |
|---|---|---|---|---|---|---|---|
| E01 Scan a directory on the Airlock host | | ● | ● | ○ | ○ | | U5 |
| E02 Upload artifacts for a scan | ○ | ● | ● | ● | ● | | U5 |
| E03 Automate scans from a CI pipeline | | ○ | ○ | ● | | | U4 |
| E04 Start scans only when they can be completed honestly | | ● | ○ | ○ | ○ | | U5 |
| E05 Trustworthy correlation | ● | ● | ● | | | | U3 |
| E06 Results that never overstate safety | ● | ● | ○ | ○ | ○ | | U5 |
| E07 Find my way around Airlock | | | | | ● | | U5 |
| E08 Watch a scan as it runs | | ● | ● | ○ | ● | | U5 |
| E09 Stop a scan | | ● | ● | ○ | ○ | | U5 |
| E10 Review scan results | ○ | ○ | | | ● | | U5 |
| E11 Export a Scan Report | | | ● | ● | ○ | | U5 |
| E12 Know whether Airlock is ready | | ○ | ○ | ○ | ● | | U5 |
| E13 Configure Airlock | | | ● | ○ | | ○ | U6 |
| E14 Run Airlock locally or in Docker | | | ○ | ○ | | ● | U6 |
| E15 Trust how Airlock is built and licensed | | | | | | ● | U6 |

Every epic has at least one unit and every unit serves at least one epic.

## Where each scenario is automated

Scenarios are listed by their titles in `../user-stories/stories.md`. "Manual" marks the checks done in the browser
pane during U5 and Build and Test, where an automated test would only re-test the framework.

| Epic | Scenario | Tested in |
|---|---|---|
| E01 | Scan an existing directory | U2 (fake Syft), U3 (Syft integration) |
| E01 | A mistyped path is refused, not reported clean | U2 |
| E01 | A path outside the allowed scan roots is refused | U2 |
| E01 | Syft fails on the directory | U2, U3 (non-zero exit mapping) |
| E02 | Upload a manifest and a container image archive | U2 (grouping, de-duplication), U4 (multipart to staging), U5 (progress bar) |
| E02 | A hostile file name cannot escape the staging folder | U3 |
| E02 | Upload limits are enforced | U2 (count and bytes), U4 (413) |
| E02 | A file without recognizable packages produces a warning | U2 |
| E02 | Python wheels and NuGet packages are read | U3 (`.zip` suffix, `.nuspec` reader), U2 (merge) |
| E02 | Image scans warn about distribution backports | U2 (warning), U3 (image source reported by Syft) |
| E02 | Staging is cleaned up however the scan ends | U2 (every exit path), U3 (folder deleted) |
| E03 | Submit and poll | U4 |
| E03 | Follow the event stream | U4 |
| E03 | A second submission while a scan runs | U4 |
| E03 | The report is not ready yet | U4 |
| E03 | Retention | U3 (registry), U4 (404) |
| E04 | A second scan is refused | U2, U5 (inline message) |
| E04 | A missing NVD key blocks scanning | U2, U4 (503), U5 (pill and Execute) |
| E04 | Syft is unavailable | U2, U4 (503), U5 (pill) |
| E04 | Nothing to scan | U2 |
| E05 | A CPE match with only lower-severity CVEs is clean | U2 |
| E05 | The likeliest candidate CPE is tried first | U1 (ranking), U2 |
| E05 | A product match finds what the CPE guesses missed | U2, U3 (`virtualMatchString` query) |
| E05 | Description-only mentions never become findings | U2, U3 (recorded S0 responses) |
| E05 | NVD's own score wins | U1 (selection), U3 (Primary and Secondary mapping) |
| E05 | A CVE without any score is listed, not counted | U1 (finding selection), U2 (warning) |
| E05 | The CVSS version is chosen and shown | U1 (selection), U3 (metric mapping), U5 (label) |
| E05 | NVD rate limit is respected | U3 |
| E06 | Some lookups fail | U2, U5 (banner) |
| E06 | Every lookup succeeds | U2 |
| E06 | A package without a version is Unverified | U2 |
| E06 | The scan time limit is reached | U2 |
| E06 | Counts add up | U1 |
| E07 | Placeholder buttons are inert | U5 |
| E07 | Navigation keeps the scan state | U5 |
| E07 | Keyboard operation | U5 (manual) |
| E07 | Severity is readable without colour | U5 |
| E08 | Every open tab receives the Live Log | U3 (broadcaster), U5 (manual, two tabs) |
| E08 | Correlation progress | U2 (events), U5 (display) |
| E08 | The terminal starts fresh for each scan | U5 |
| E08 | Export the log | U5 |
| E09 | Stop during SBOM generation | U3 (process-tree kill), U2 |
| E09 | Stop during correlation | U2 |
| E09 | The server keeps running | U4 |
| E09 | Dismissing the confirmation | U5 |
| E09 | Same behaviour on Windows | U3 (integration on the Windows dev machine), Build and Test (Linux container) |
| E10 | Risk shows the highest severity | U1 (rule), U5 (display) |
| E10 | Product matches are labelled | U5 |
| E10 | Filter by CVE id | U5 |
| E10 | Two versions of the same package | U5 |
| E10 | Cards count findings and packages | U1 (summary), U5 |
| E10 | The result survives a refresh | U5 |
| E10 | Rescanning uploads after a refresh | U5 |
| E11 | The UI and the API produce the same report | U4 (same endpoint serves both) |
| E11 | An Incomplete scan's report lists unverified packages | U3 (renderer) |
| E11 | JSON export matches the API | U4 (same endpoint serves both) |
| E11 | No export while the scan runs | U4 (409), U5 (disabled buttons) |
| E12 | Ready | U2, U5 |
| E12 | Not ready | U2, U5 |
| E12 | Connection lost | U5 (manual) |
| E12 | Health over the API | U4 |
| E13 | The key never reaches the logs | U3 |
| E13 | An invalid option stops startup | U3 (validators), U4 (startup) |
| E13 | Allowed scan roots are optional | U2 |
| E13 | Console verbosity is independent of the Live Log | U3 |
| E14 | Run locally on Windows | U5 (manual), documented in U6 |
| E14 | Run with Docker Compose | U6 |
| E14 | Same results on both platforms | Build and Test |
| E15 | Pull requests are verified | U6 |
| E15 | Code scanning | U6 |
| E15 | Licensing is documented | U6 |

## Per-unit view

| Unit | Main work for | Contributes to |
|---|---|---|
| U1 Domain | E05, E06 | E02, E10 |
| U2 Application | E01, E02, E04, E05, E06, E08, E09 | E03, E10, E12 |
| U3 Infrastructure | E01, E02, E05, E08, E09, E11, E13 | E03, E04, E06, E12, E14 |
| U4 Web API | E02, E03, E11 | E01, E04, E06, E08, E09, E12, E13, E14 |
| U5 Web UI | E02, E07, E08, E10, E12 | E01, E04, E06, E09, E11 |
| U6 Delivery | E14, E15 | E13 |
