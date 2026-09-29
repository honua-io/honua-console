# Vendored: vega@6.4.0

Committed third-party browser assets, served from the Console's own origin. The Console must not
fetch executable code from a CDN at page load (honua-console#333): a customer without egress gets a
broken surface, and the CSP would have to admit a script origin nothing else needs.

| | |
| --- | --- |
| Package | `vega@6.4.0` |
| License | BSD-3-Clause (see `LICENSE.txt`) |
| Source | https://registry.npmjs.org/vega/-/vega-6.4.0.tgz |
| Tarball integrity | `sha512-rHXCq2LpEWCLMjXBEj7hocY9G0/i5TGYRayGGSj06VTF4+l2IG2Z7oeZKCjWbUh/7hTE/mVhbaOPB72wCElh6g==` |

**Do not edit these files by hand.** They are byte-for-byte copies of the published npm tarball
contents, and `npm test` verifies their digests against `scripts/vendored-assets.lock.json`.

To update:

```sh
# 1. bump "version" in scripts/vendored-assets.json
node scripts/vendor-assets.mjs --update
# 2. commit the rewritten assets together with scripts/vendored-assets.lock.json
```
