---
status: accepted
---

# Product matching replaces keyword search

When none of a package's candidate CPEs has NVD records, Airlock asks NVD for the CVEs whose configurations name the
package's product — from any vendor — at the package's version (`virtualMatchString=cpe:2.3:a:*:<name>:<version>`),
instead of searching CVE descriptions for "name version" as the original did. The S0 spike showed that keyword search
both invents findings (GeoServer and Laminas CVEs for requests 2.25.1) and misses real ones (CVE-2021-44906 for
minimist 1.2.5), while product matching found the real CVEs, respected version ranges and returned none of the
unrelated ones. This supersedes the keyword fallback approved in requirements question Q7.

## Consequences

- Products that share a name across vendors can still collide, so names too generic to identify a product are not
  product-matched (domain rule BR-09: names under three characters and a list of generic words such as `core`,
  `utils` and `auth`).
- Findings carry the Product match method; "keyword match" is retired from the glossary.
