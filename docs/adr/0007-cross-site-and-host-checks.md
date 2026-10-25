---
status: accepted
---

# Refuse cross-site requests and unknown host names instead of authenticating

Airlock has no authentication: it is a local, single-user tool, and anyone who can reach its port may use it, an
accepted risk. That reasoning left out the browser. A web page the analyst visits can make the browser send `POST`
requests to a local Airlock — multipart forms need no CORS preflight — to start a scan of any directory on the host,
cancel one or press Emergency Stop. A site that points its own host name at 127.0.0.1 (DNS rebinding) is even
same-origin to the browser, so it can read the answers too, scan results included. Airlock therefore refuses with 403
any `POST` that the browser marks as cross-site, through `Sec-Fetch-Site` or an `Origin` that differs from the host,
and answers only the host names in `AllowedHosts`, by default `localhost`, `127.0.0.1` and `[::1]`. Clients that are
not browsers, such as curl and CI jobs, send neither header and are unaffected.

## Considered Options

- Authentication, with an API key or a login: it protects everything, but every client — the UI, curl, CI — would have
  to carry a credential, which the requirements ruled out for a local tool.
- Antiforgery tokens on the API: they stop forged forms but not DNS rebinding, and every API client would first have
  to fetch a token.
- A required custom request header: it forces a CORS preflight, but every curl and CI call would have to add it.
- The cross-site check alone, answering any host name: it leaves DNS rebinding open.

## Consequences

- An operator who reaches Airlock by any other name — a server's name, a CI service such as `airlock`, an IP address —
  must add it to `AllowedHosts`; `*` answers every name and accepts the rebinding risk.
- Another web application on a different port of `localhost` is refused too, because the browser marks its requests
  `same-site`, not `same-origin`.
- Browsers too old to send `Sec-Fetch-Site` are still covered by the `Origin` check.
