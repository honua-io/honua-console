# Vendored: vega-embed@7.1.0

Committed third-party browser assets, served from the Console's own origin. The Console must not
fetch executable code from a CDN at page load (honua-console#333): a customer without egress gets a
broken surface, and the CSP would have to admit a script origin nothing else needs.

| | |
| --- | --- |
| Package | `vega-embed@7.1.0` |
| License | BSD-3-Clause (see `LICENSE.txt`) |
| Source | https://registry.npmjs.org/vega-embed/-/vega-embed-7.1.0.tgz |
| Tarball integrity | `sha512-ZmEIn5XJrQt7fSh2lwtSdXG/9uf3yIqZnvXFEwBJRppiBgrEWZcZbj6VK3xn8sNTFQ+sQDXW5sl/6kmbAW3s5A==` |

**Do not edit these files by hand.** They are byte-for-byte copies of the published npm tarball
contents, and `npm test` verifies their digests against `scripts/vendored-assets.lock.json`.

To update:

```sh
# 1. bump "version" in scripts/vendored-assets.json
node scripts/vendor-assets.mjs --update
# 2. commit the rewritten assets together with scripts/vendored-assets.lock.json
```
