# U3 Infrastructure — NFR Requirements Plan

**Stage**: CONSTRUCTION — U3 Infrastructure — NFR Requirements (Standard depth)
**Started**: 2026-10-23T13:56:41Z

## Inputs

- `inception/requirements/requirements.md` (NFR-02 to NFR-09)
- `construction/u3-infrastructure/functional-design/` (IR-01 to IR-26 and the rule-to-test map)
- `construction/u2-application/code/code-generation-summary.md` (the constraints handed to U3) and
  `construction/u2-application/nfr-requirements/nfr-requirements.md` (U2-NFR-03 to U2-NFR-05, U2-NFR-12)
- `inception/application-design/component-methods.md` (the Infrastructure section) and `component-dependency.md`
  (architecture rules 3 and 4)
- `construction/spikes/spike-results.md` (N9 throttling; S10 scale: 281 packages, about 290 KB of Syft JSON)

## Steps

- [x] Identify the quality requirements that are specific to the Infrastructure project
- [x] `construction/u3-infrastructure/nfr-requirements/nfr-requirements.md` (U3-NFR-01 to U3-NFR-14)
- [x] `construction/u3-infrastructure/nfr-requirements/tech-stack-decisions.md`
- [x] Present for approval (approved 2026-10-23T14:21:08Z)

No open questions: every item follows from approved requirements, the U3 functional design and U2's constraints. The
new targets it sets are listed for review at the checkpoint:

1. NVD pacing: requests start at least 600 ms apart, application-wide and retries included, so no 30-second span
   holds more than 50. Airlock uses its own limiter on `TimeProvider`; the built-in `SlidingWindowRateLimiter` can let
   more than 50 requests into some 30-second spans.
2. NVD time: each lookup ends within 2 minutes, every page and attempt included (U2's constraint). Waiting for a turn
   counts towards that bound, never towards an attempt's timeout.
3. Syft: a run ends within 1 second of cancellation or of its time limit, process tree included; only the last 4 KB
   of its error output are kept; Syft starts without the NVD key in its environment.
4. Large inputs: a generated 256 MiB upload is staged with under 16 MB allocated; at most 1 MB of a `.nuspec` is read.
5. Performance: Syft's 281-package output parsed in under 100 ms and 10,000 packages in under 2 seconds; a
   2,000-record NVD page read and mapped in under 1 second; a 500-finding PDF in under 5 seconds.
6. Coverage of `Airlock.Infrastructure`: at least 85 % of lines and 80 % of branches, integration tests included
   (U2 is 90 % and 85 %; the Infrastructure has branches for the other operating system that one machine cannot
   reach).
7. Unit tests need neither the network nor Syft; tests with the real Syft are `Category=Integration` and skip when it
   is absent.
8. Recorded NVD response headers are trimmed to the ones the tests read before they are committed.
