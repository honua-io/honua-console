## Smoke Evidence — Public Open-Data Pages (`honua-console#4`)

This note records the public open-data page behavior covered in CI. Update it
whenever a change touches public item routing, open-data filtering, API/download
affordances, DCAT/data.json exposure, or publishing-quality readiness.
Source-repo history is `honua-portal#17`; ongoing ownership is
`honua-console#4`.

### Flows Covered

| Flow | Coverage | Evidence file |
| --- | --- | --- |
| Public collection | `/public` and `/share/public` load without a session. The emitted card links currently use `/public/items/:idOrSlug`, page through public catalog responses before applying the open-data filter, list only items with `access.sharing === "public"` and `access.openData === true`, and omit public maps that are not open-data plus org/private items. | `src/open-data/OpenDataItemPage.test.tsx`, `src/router.tsx`, `tests/unit/AppShell.test.tsx`, `tests/smoke/shell.spec.ts` |
| Public item page | `/public/items/city-parcels-2026` renders without a session and shows title, summary, publisher/contact, license, attribution, timestamps, preview extent, download/API rows, API examples, and Schema.org Dataset JSON-LD. `/share/public/items/:idOrSlug` is served by the same router component as the Console IA alias. | `src/open-data/OpenDataItemPage.test.tsx`, `src/router.tsx`, `tests/unit/AppShell.test.tsx`, `tests/smoke/shell.spec.ts` |
| Document distribution | `/public/items/parcels-data-dictionary` exposes the document download URL and a copyable `curl -L` example. | `src/open-data/OpenDataItemPage.test.tsx` |
| Private and non-open-data denial | Private, unauthorized, missing, and public-but-not-open-data items render the generic "Public item not found" surface and do not leak private titles. | `src/open-data/OpenDataItemPage.test.tsx`, `tests/smoke/shell.spec.ts` |

Public-link tokenized open-data routes are not implemented in `#4`: the public
collection and item page require `sharing === "public"` plus
`access.openData === true`.

Service metadata docs fallback remains represented by fixtures rather than a
server-backed docs endpoint in `#4`: `Permit Status Feed (no docs URL)` models a
public service that has no `describedBy` URL, while `Honua Events Stream` models
the `Honua:API:v1` unsupported-docs branch and stays out of the public
collection because it is not public open data.

### How To Run Locally

```bash
npm run test
npm run smoke
```

### Deferred Out Of `honua-console#4`

- DCAT-US 3.0 / `data.json` generation and validation is deferred to a
  follow-up Console open-data ticket.
- Publishing readiness states and durable usage analytics roll up under
  `honua-console#7` (shared metadata/RBAC wiring).
- Cross-surface parity smoke (publish → catalog → Studio → share/embed)
  belongs to `honua-console#9`.
