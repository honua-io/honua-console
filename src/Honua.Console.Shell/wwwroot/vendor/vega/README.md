# Vendored: vega@5.33.0

Committed third-party browser assets, served from the Console's own origin. The Console must not
fetch executable code from a CDN at page load (honua-console#333): a customer without egress gets a
broken surface, and the CSP would have to admit a script origin nothing else needs.

| | |
| --- | --- |
| Package | `vega@5.33.0` |
| License | BSD-3-Clause (see `LICENSE.txt`) |
| Source | https://registry.npmjs.org/vega/-/vega-5.33.0.tgz |
| Tarball integrity | `sha512-jNAGa7TxLojOpMMMrKMXXBos4K6AaLJbCgGDOw1YEkLRjUkh12pcf65J2lMSdEHjcEK47XXjKiOUVZ8L+MniBA==` |

**Do not edit these files by hand.** They are byte-for-byte copies of the published npm tarball
contents, and `npm test` verifies their digests against `scripts/vendored-assets.lock.json`.

To update:

```sh
# 1. bump "version" in scripts/vendored-assets.json
node scripts/vendor-assets.mjs --update
# 2. commit the rewritten assets together with scripts/vendored-assets.lock.json
```
