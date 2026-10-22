---
status: accepted
---

# Scans fail closed: Unverified packages and Incomplete scans

The original Airlock swallowed Syft, NVD and configuration errors, so an NVD outage, a mistyped path or a missing API
key all produced a "clean" report: the worst failure for a gate that admits software into a controlled environment.
Airlock now marks any package whose lookups still fail after retries as **Unverified** (never clean) and finishes such
scans as **Incomplete**; Syft failures and unusable inputs **fail** the scan, and a missing NVD key stops a scan from
starting at all.

## Considered Options

- Abort the whole scan on the first NVD failure that survives retries: safe, but throws away useful partial results.
- Keep failing open, as the original did: rejected as unsafe.
