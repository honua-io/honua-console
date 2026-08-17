# Vendored: vega-lite@5.23.0

Committed third-party browser assets, served from the Console's own origin. The Console must not
fetch executable code from a CDN at page load (honua-console#333): a customer without egress gets a
broken surface, and the CSP would have to admit a script origin nothing else needs.

| | |
| --- | --- |
| Package | `vega-lite@5.23.0` |
| License | BSD-3-Clause (see `LICENSE.txt`) |
| Source | https://registry.npmjs.org/vega-lite/-/vega-lite-5.23.0.tgz |
| Tarball integrity | `sha512-l4J6+AWE3DIjvovEoHl2LdtCUkfm4zs8Xxx7INwZEAv+XVb6kR6vIN1gt3t2gN2gs/y4DYTs/RPoTeYAuEg6mA==` |

**Do not edit these files by hand.** They are byte-for-byte copies of the published npm tarball
contents, and `npm test` verifies their digests against `scripts/vendored-assets.lock.json`.

To update:

```sh
# 1. bump "version" in scripts/vendored-assets.json
node scripts/vendor-assets.mjs --update
# 2. commit the rewritten assets together with scripts/vendored-assets.lock.json
```
