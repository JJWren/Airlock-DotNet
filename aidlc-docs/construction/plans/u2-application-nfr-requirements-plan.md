# U2 Application — NFR Requirements Plan

**Stage**: CONSTRUCTION — U2 Application — NFR Requirements (Standard depth)
**Started**: 2026-10-22T02:19:36Z

## Inputs

- `inception/requirements/requirements.md` (NFR-02 to NFR-09)
- `construction/u2-application/functional-design/` (AR-01 to AR-35)
- `inception/application-design/services.md` (lifetimes, concurrency, cancellation, streams) and
  `component-dependency.md` (architecture rules 2, 4 and 6)
- `construction/u1-domain/nfr-requirements/` (the solution-wide stack)
- `construction/spikes/spike-results.md` (scale: 281 packages, about 1,100 lookups, about 11 minutes at the keyed rate)

## Steps

- [x] Identify the quality requirements that are specific to the Application project
- [x] `construction/u2-application/nfr-requirements/nfr-requirements.md` (U2-NFR-01 to U2-NFR-14)
- [x] `construction/u2-application/nfr-requirements/tech-stack-decisions.md`
- [x] Present for approval (approved 2026-10-22T10:44:54Z)

No open questions: every item follows from approved requirements and the U2 functional design. The new targets it
sets are listed for review at the checkpoint:

1. Coverage of `Airlock.Application`: at least 90 % of lines and 85 % of branches (U1 is 95 % and 90 %; the
   Application's concurrency guards have defensive branches that fakes cannot always reach).
2. Stopping: the final status within 2 seconds when the ports honour cancellation; `CancelScan` waits at most
   10 seconds.
3. Outage: `Correlation.OutageThreshold` between 3 and 20; an unreachable NVD ends a scan within 5 minutes of the first
   failed lookup.
4. `Correlation.MaxParallelism` between 1 and 16.
5. Overhead with an instant fake catalog: 281 packages in under 1 second, 10,000 in under 10 seconds.
6. Concurrency stress test: 100 concurrent reservations, exactly one winner, repeated 50 times.
7. An architecture test bans clock reads in the Application, as in the Domain.
8. NSubstitute is not used in U2; every port gets a hand-written fake.
