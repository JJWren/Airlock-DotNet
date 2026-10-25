# Airlock API problems

Every error answer of the Airlock API (`/api/v2`) is an RFC 9457 problem, `application/problem+json`, with five
members:

```json
{
  "type": "https://github.com/JJWren/Airlock-DotNet/blob/main/docs/api/problems.md#scan-in-progress",
  "title": "Scan in progress",
  "status": 409,
  "detail": "A scan is already in progress",
  "traceId": "00-6f1c2a7d9e0b4c3f8a5d2e1b0c9f7a6d-3b2a1c0d9e8f7a6b-00"
}
```

`type` identifies the kind of problem and links to its section below; compare it, not `title` or `detail`, when a
script must tell problems apart. `detail` says what happened in words meant for a person. `traceId` matches the line
the server logged for the request. No problem carries exception text, a stack trace or an internal path.

## scan-in-progress

**409** · Scan in progress. Airlock runs one scan at a time, and another scan — or another client's upload — holds
the slot. Retry later, or follow the running scan; `POST /api/v2/system/stop` ends it whoever started it.

## not-ready

**503** · Airlock is not ready. No NVD API key is configured, or Syft is not available; `detail` gives every reason,
such as "NVD API key not configured". Fix the configuration; `GET /api/v2/system/health` shows the same checks.

## invalid-target

**400** · Invalid scan target. The directory does not exist, is a file, is not absolute, lies outside the allowed scan
roots, or is not valid text; or the request names a directory and uploads artifacts too, or neither. Send either
`targetPath` or `artifacts`.

## too-many-artifacts

**400** · Too many artifacts. A scan takes at most 25 files by default. Split the upload into several scans.

## too-large

**413** · Upload too large. The artifacts pass the per-scan byte limit (2 GB by default), or the body passes the
server's own limit. A client still sending may see the connection close before it reads this answer.

## upload-stopped

**409** · Upload stopped. Emergency Stop ended the upload before its scan started; nothing was recorded. Submit again.

## scan-not-found

**404** · Scan not found. There is no scan with this id, or it is no longer retained: finished scans are kept for 24
hours, at most 20 of them, and none survive a restart.

## scan-not-finished

**409** · Scan still running. The PDF report exists once the scan has finished. Follow the scan's events, or poll it,
and ask again.

## scan-already-finished

**409** · Scan already finished. There is nothing left to cancel.

## scan-has-no-results

**409** · Scan has no results. The scan failed or was cancelled, so it has no report; `GET /api/v2/scans/{id}` shows
why.

## invalid-request

**400** · Invalid request. The submission is not usable as sent: a field other than `targetPath` and `artifacts`, a
second `targetPath`, an artifact without a file name, a missing multipart boundary, or a body that is not valid
`multipart/form-data`.

## unsupported-media-type

**415** · Unsupported media type. Scans are submitted as `multipart/form-data`, for example
`curl -F targetPath=/uploads/acme-app http://localhost:8080/api/v2/scans`.

## cross-site-request

**403** · Cross-site request refused. A browser marked the request as coming from another website (`Sec-Fetch-Site`, or
an `Origin` that differs from the host). Airlock has no authentication, so it accepts state-changing requests only from
its own pages and from clients that are not browsers, such as curl and CI jobs.

## not-found

**404** · Not found. No API endpoint answers at this address.

## method-not-allowed

**405** · Method not allowed. The endpoint exists but not for this method; the `Allow` header lists the ones it accepts.

## unexpected-error

**500** · Unexpected error. Something failed that Airlock did not expect. The server log has the details under the same
`traceId`.
