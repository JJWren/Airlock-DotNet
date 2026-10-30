# U6 Delivery — NFR Design Plan

**Stage**: CONSTRUCTION — U6 Delivery — NFR Design (Minimal depth)
**Started**: 2026-10-29T01:51:00Z

## Inputs

- `construction/u6-delivery/nfr-requirements/` (U6-NFR-01 to U6-NFR-18, the tech-stack decisions)
- `construction/u6-delivery/functional-design/` (DR-01 to DR-31, the three flows, the rule-to-check map)
- U4's infrastructure design and deployment architecture (listening addresses, configuration sources, data
  protection), `appsettings.json`, `Directory.Packages.props` and U3's `scripts/install-tools.sh`
- Facts checked on 2026-10-29:
  - **Airlock's own build**, started from its Debug output in Production with a scratch staging root, nothing
    downloaded:
    - with `ASPNETCORE_URLS=http://127.0.0.1:18081` it still listened on `http://localhost:8080`, the `Urls` in
      `appsettings.json`;
    - with `URLS=http://127.0.0.1:18082` it listened there;
    - with `URLS` and `ASPNETCORE_HTTP_PORTS` both set, it logged a warning from
      `Microsoft.AspNetCore.Hosting.Diagnostics` that `URLS` overrides `HTTP_PORTS`.
  - **ASP.NET Core's antiforgery** (`AntiforgeryLoggerExtensions.cs` in dotnet/aspnetcore): a cookie it cannot read
    is logged at Error, as `TokenDeserializeException`.
  - **zizmor's audits** (docs.zizmor.sh):
    - since v1.20.0, `unpinned-uses` requires a commit SHA for every action by default, GitHub's own included;
    - `forbidden-uses` takes an allow-list;
    - `dependabot-cooldown` flags an update entry with no cooldown, or one under 7 days;
    - `impostor-commit` and `known-vulnerable-actions` need the network.
  - **Grype** (the configuration reference at oss.anchore.com; the CLI's source):
    - `only-fixed` and `fail-on-severity` are configuration keys;
    - `--only-fixed` adds ignore rules, and the JSON report lists ignored matches under `ignoredMatches`;
    - an ignore rule has an optional `reason` and no date;
    - a database built more than 120 hours ago is refused.
  - **U3's install scripts** compare Syft's archive with the checksums file of the same release, downloaded at the
    same moment.

## Steps

- [x] Check the facts the patterns depend on (above)
- [x] `construction/u6-delivery/nfr-design/nfr-design-patterns.md` — a pattern for each of U6-NFR-01 to U6-NFR-18,
      the release's steps, the writable places, and what was found
- [x] `construction/u6-delivery/nfr-design/logical-components.md` — the pieces this design adds, how the workflows
      share them (validated Mermaid, with a text alternative), the checks, and what is left to Infrastructure Design
- [x] Bring in line: DR-02 to DR-04, DR-11, DR-13, DR-16 and DR-22, the domain entities and the flows; U6-NFR-01,
      U6-NFR-02, U6-NFR-07 and U6-NFR-09 and the tech-stack decisions; U4's infrastructure design and deployment
      architecture; U6 in `unit-of-work.md`
- [x] Present for approval (2026-10-29T02:19:55Z)

No question is open: each choice below follows from an approved requirement or a checked fact.

## Choices made in this design (object at the gate if any is wrong)

1. **The listening address** (a finding). The image sets `URLS=http://+:8080` and clears the base image's
   `ASPNETCORE_HTTP_PORTS`. DR-02's `ASPNETCORE_URLS` loses to `appsettings.json` and would have left the container
   listening on its own loopback. This corrects DR-02 and U4's infrastructure design.
2. **The data-protection keys persist on the volume**, in a folder beside the staging root. With keys in memory, as
   U4 left them, every restart would log an Error for each browser's antiforgery cookie issued before it.
3. **Checksums are committed.** Every tool CI downloads is compared with a SHA-256 or digest kept in this repository,
   not only with a checksums file fetched beside it. U3's install scripts gain Syft's archive hashes (DR-04).
4. **A release builds one image.** `release.yml` calls only CI's lint and test jobs. Its publishing job builds once,
   and runs the same smoke test and scan as CI's image job (DR-11, DR-22).
5. **Verified before tagged.** The publishing job verifies both attestations of the pushed digest with
   `gh attestation verify` before it sets a tag. It then checks that the tag names that digest (U6-NFR-07).
6. **The smoke test runs through the compose file.** It uses `docker-compose.yml` plus a narrow override, so the
   hardening it tests is the file users run.
7. **Grype's whole policy is in `.grype.yaml`**: `only-fixed`, `fail-on-severity: high` and the acceptances.
   - Each acceptance's `reason` begins `review-by YYYY-MM-DD:`, since Grype's rules have no date field.
   - A database more than five days old fails the job.
8. **Stay on .NET 10 LTS.** Dependabot ignores major updates of the .NET SDK, Microsoft's .NET images and the
   `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` packages. .NET 11, an STS release due in November 2026, is
   never proposed (U6-NFR-02, DR-16).
9. **zizmor**:
   - runs online, with the job's read-only token;
   - keeps the workflows to GitHub's own actions through `forbidden-uses`;
   - also reads `dependabot.yml`. The SDK's update entry, which has no cooldown on purpose, is the one finding
     `.github/zizmor.yml` accepts at the start.
   - Accepted findings live only in that file: a repository test rejects inline ignore comments.
10. **Fixed concurrency groups.** They are named `ci-` or `release-` plus the ref, so the CI jobs a release calls
    never share a group with the release.
11. **The image's size** goes in every run summary, with a warning annotation past 150 MiB. It does not fail the run.
12. **One list of expected start warnings.** The smoke test fails on any other warning, and a repository test checks
    that the README explains each one.
