# Build and Test Plan

**Stage**: CONSTRUCTION — Build and Test (Standard depth, per Workflow Planning)
**Started**: 2026-10-30T16:08:25Z

Build and Test writes the build and test instructions for all six units, and runs the checks the earlier stages left
to it, recording each result. Workflow Planning set its depth: the five instruction files, covering Syft's integration
tests, the API's integration tests, NVD rate-limit compliance, a large upload, security checks (path traversal, error
bodies) and an end-to-end pass in the browser pane.

## Inputs

- What each unit handed on:
  - U1-NFR-05, U2-NFR-10, U3-NFR-10 and U4-NFR-08: performance targets "measured in Build and Test", with bounds ten
    times wider asserted in the tests;
  - U3: the install scripts' download path, on Windows and Linux;
  - U5: findings on screen with a real key, the axe-core scan, copying over plain HTTP from another host name, reduced
    motion, and timings in a visible pane (U5-NFR-02, -04, -05, -06, -07, -10, -11);
  - U6: E14's three scenarios with a real key; the live NVD test; stopping the container during a scan; time zones in
    the container; the quick start from a fresh clone; CI's first run and the first release, after you push.
- This machine, checked on 2026-10-30 without changing anything:
  - **No NVD key is in user-secrets**: Airlock, run in Development, reported "NVD API key not configured".
  - Ports 8080 and 18080 are free.
  - The builder `airlock-dotnet` is stopped, with its cache; `airlock-dotnet:smoke` and the tool images are here.
- The worked example of U2 and U4: `requirements.txt` with flask 0.12.2, jinja2 2.10, pyyaml 5.3 and requests 2.25.1.
  S0 recorded pyyaml's two CRITICAL CVEs, flask's four CVEs by product match, jinja2's CVE-2019-8341 by product match,
  and requests clean. Today's NVD may hold more, and the result is recorded as found.
- The large scan: Syft's own Windows binary from S0 (281 packages, S0 S10).

## Ground rules

- **Nothing is pushed.** Commits only as Q-BT-2 says.
- **Your key is never read or printed by me.** You store it yourself. Airlock reads it from user-secrets or from a
  `.env` you write, and no command I run echoes configuration: no `docker compose config`, no `docker inspect` of the
  environment, no `user-secrets list`. Airlock's logs never contain it (U3-NFR-07), and the checks confirm that.
- **NVD traffic.** With your key, about 1,200 requests in all, paced by Airlock at one per 600 ms, within NVD's keyed
  limit: the worked example from source and in the container, the end-to-end pass, and one scan of the 281-package
  binary. Without a key: the live test's single lookup.
- **Docker names**: `airlock-bt-…` for everything this stage creates, checked unused first; `airlock` for E14's
  compose run in the repository, as a user would. Nothing else is stopped or removed.
- **Downloads**, made only once you approve this plan, each into a new folder and checked before use:
  - **Syft v1.55.0's archives**, from github.com/anchore/syft, each with its release's checksums file: for Windows,
    `syft_1.55.0_windows_amd64.zip`, 28.6 MiB; for Linux, `syft_1.55.0_linux_amd64.tar.gz`, 29.8 MiB. The install
    scripts fetch them in the fresh clone, and check them against the SHA-256 they carry, which is part of the test.
  - **axe-core 4.14.0** (MPL-2.0, released 2026-10-05), its npm tarball from registry.npmjs.org: about 1 MiB, 3.27 MiB
    unpacked. It is checked against the registry's SHA-512, and only `axe.min.js` (652 KB) is injected into the pane.
  - **The SDK and runtime images and Airlock's NuGet packages**, through Docker's default builder, as a new user's
    Docker would build the fresh clone: about 480 MiB (SDK 341.9 MiB, runtime 69.2 MiB, NuGet 66.5 MiB), from
    mcr.microsoft.com and nuget.org.
- **Warnings as errors** stay; any test change keeps the build at zero warnings.

## Design refinements made while planning

1. **The performance tests write what they measure.** They report their timings only when they fail, and Build and
   Test is to record the times themselves. Each performance test (U2's, U3's four, U4's three, U5's large results)
   writes its measurement to the test output, which the TRX keeps. U1-NFR-05 has no test yet, so a Domain test is
   added: `ScanSummary.From` and the finish of a scan with 10,000 packages, bounded at ten times the target, like the
   others. Test code only.
2. **The ten-tab measurement (U5-NFR-02) shares the large keyed scan.** A keyed scan is paced by NVD's limit, 600 ms a
   request, so its length without tabs is known from its request count. The 281-package scan runs once, with ten tabs
   following it, and its length is compared with that pace. A second run without tabs would double the NVD traffic and
   measure only the network.
3. **Reduced motion is checked in the stylesheets.** The pane cannot emulate `prefers-reduced-motion`, so the check
   reads the page's CSS rules for that media query and confirms they stop the animations.

## Steps

### A. Prerequisites
- [x] A1 Your answers; your key stored as Q-BT-1 says (you run the command; I do not see the key)
- [x] A2 The commits, if Q-BT-2 says so
- [x] A3 Ports and `airlock-bt-…` names checked free

### B. The instructions
- [x] B1 `construction/build-and-test/build-instructions.md`: the SDK, Syft, the solution, the image, compose, the C#
      and bash scripts, Windows and Linux
- [x] B2 `unit-test-instructions.md`: every test project, filters, coverage and its checker, the repository tests
- [x] B3 `integration-test-instructions.md`: Syft's integration tests, the API's, the live NVD test, E14's three
      scenarios, the security checks, stopping during a scan, the end-to-end pass in the browser, accessibility, CI's
      first run and the first release
- [x] B4 `performance-test-instructions.md`: every performance target, how it is measured and against what
- [x] B5 `build-and-test-summary.md`: what ran, its results, and what is left

### C. Build
- [x] C1 The solution in Debug and Release: zero warnings
- [x] C2 A fresh clone, from the commit or from the files a commit would hold (Q-BT-2): the README's quick start as
      written, `docker compose up` building with Docker's default builder, then the UI on `http://localhost:8080`
- [x] C3 The install scripts in that clone: `install-tools.ps1` on Windows and `install-tools.sh` in WSL, each
      downloading and checking Syft v1.55.0; then a corrupted archive refused by each

### D. Unit tests
- [x] D1 Refinement 1, then every test project with coverage: counts, the coverage checker, the measured times

### E. Integration
- [x] E1 The live NVD test (`AIRLOCK_LIVE_NVD=1`), keyless
- [x] E2 E14 from source on Windows, with your key: ONLINE; the worked example scanned; its findings recorded
- [x] E3 E14 with compose, with your key in `.env`: `docker compose up -d --build`, ONLINE, the same folder scanned at
      `/uploads`
- [x] E4 The same packages and findings on both (E14 "Same results on both platforms")
- [x] E5 Security, against the container: a `../../evil.txt` upload staged as `evil.txt`; a cross-site `POST` refused
      403; an unknown host refused 400; a path outside `/uploads` refused 400; every error a problem without a stack
      trace; the key in no log line
- [x] E6 Stopping the container during a scan: the scan cancelled within the 10-second grace; after the next start, no
      scan folder left on the volume
- [x] E7 The end-to-end pass in the browser pane against the container: findings on screen (cards, grid, filter,
      detail, CVE links, CVSS), the exports, Rescan, Emergency Stop, refreshing a report, times in the browser's zone
      (U6-NFR-08), the PDF's labelled UTC
- [x] E8 Accessibility: axe-core on Control Center, Active Logs and Reports; reduced motion (refinement 3)
- [x] E9 Copying over plain HTTP from another host name, as Q-BT-3 says

### F. Performance
- [x] F1 U1-NFR-05, U2-NFR-10, U3-NFR-10 and U4-NFR-08, from D1's measurements
- [x] F2 The 281-package scan with your key: NVD's pace (no 30-second span over 50 requests, no 429), its length
      against that pace with ten tabs following it (refinement 2)
- [x] F3 U5-NFR-04: 2,000 inventory rows in the pane, from a generated lock file scanned with NVD unreachable: drawn,
      filtered and selected against 1 s, 300 ms and 100 ms
- [x] F4 U5-NFR-05: a 1 GiB upload through the pane, staged by the page's script, while changing pages
- [x] F5 U5-NFR-06: OFFLINE within 5 seconds of stopping the server; Reload offered after it starts again
- [x] F6 U5-NFR-11: a cold load of Control Center from the container: requests to other hosts, bytes, time to
      interactive

### G. Clean-up
- [x] G1 This stage's containers, volumes and images removed; the fresh clone deleted; the builder `airlock-dotnet`
      and its cache removed, since this is its last use; `airlock-dotnet:smoke` removed; the pulled images kept

### H. Documentation
- [x] H1 Record every result in `build-and-test-summary.md`; tick this plan; update `aidlc-state.md` and `audit.md`

Left after this stage, whatever the answers: CI's first run, CodeQL, Dependabot, the Windows runner, the first release
and its attestations, all after you create `JJWren/Airlock-DotNet` and push. The instructions say what to check then.

## Questions

### Q-BT-1 — Your NVD key
No key is in user-secrets. The keyed checks are E2 to E4, E7, F2 and part of E5.

A) You store it twice, yourself: `dotnet user-secrets set Airlock:Nvd:ApiKey <your-key> --project src/Airlock.Web`,
   and a `.env` in the repository from `.env.example`, with your key and `SCAN_UPLOAD_PATH` set to the folder I name.
   I never read either (Recommended)
B) Only user-secrets: the checks from source run; those in the container run without a key, and E3, E4 and F2 stay in
   the instructions for you
C) No key: every keyed check stays in the instructions for you
D) Other

[Answer]: A (2026-10-30T16:42:36Z)

### Q-BT-2 — Commit first?
The quick start from a fresh clone needs the U6 work in a commit, and CI needs commits to push.

A) Make the two proposed commits now, on `feature/u6-delivery`: the U6 design documents with the documents they
   amended and the audit entries since U5's commits; then the U6 code. The clone in C2 clones that commit. Nothing is
   pushed (Recommended)
B) No commit: C2 copies the files a commit would hold, and the commits wait for your word
C) Other

[Answer]: A (2026-10-30T16:42:36Z)

### Q-BT-3 — Copying over plain HTTP from another host name (U5)
Browsers allow the clipboard only on secure origins, and `localhost` is one. The copy buttons' fallback for plain HTTP
can be seen only from another host name, which means Airlock answering on this machine's network address.

A) For that one check, the container's port is published on this machine's network address, with `AllowedHosts`
   naming it, then put back on `127.0.0.1`. For that minute an unauthenticated Airlock is reachable from your network,
   and Windows may ask whether Docker may use the network, which is your choice (Recommended)
B) Skip it here; the instructions describe it for you
C) Other

[Answer]: A (2026-10-30T16:42:36Z)

### Plan approval
A) Request Changes
B) Approve and run, with the downloads above (about 540 MiB), about 1,200 NVD requests with your key, and the test
   changes of refinement 1

[Answer]: B (approved 2026-10-30T16:42:36Z)
