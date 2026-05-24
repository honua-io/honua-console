## Smoke Evidence — Public Open-Data Pages (`honua-console#4`)

This note records the public open-data page behavior covered in CI. Update it
whenever a change touches public item routing, open-data filtering, API/download
affordances, DCAT/data.json exposure, or publishing-quality readiness.
Source-repo history is `honua-portal#17`; ongoing ownership is
`honua-console#4`.

### Flows Covered

| Flow | Coverage | Evidence file |
| --- | --- | --- |
| Public collection | `/public` loads without a session, lists only items with `access.sharing === "public"` and `access.openData === true`, and links cards to `/public/items/:idOrSlug`. Public maps that are not open-data and org/private items are omitted. | `src/open-data/OpenDataItemPage.test.tsx`, `tests/unit/AppShell.test.tsx`, `tests/smoke/shell.spec.ts` |
| Public item page | `/public/items/city-parcels-2026` renders without a session and shows title, summary, publisher/contact, license, attribution, timestamps, preview extent, download/API rows, API examples, and Schema.org Dataset JSON-LD. | `src/open-data/OpenDataItemPage.test.tsx`, `tests/unit/AppShell.test.tsx`, `tests/smoke/shell.spec.ts` |
| Document distribution | `/public/items/parcels-data-dictionary` exposes the document download URL and a copyable `curl -L` example. | `src/open-data/OpenDataItemPage.test.tsx` |
| Private and non-open-data denial | Private, unauthorized, missing, and public-but-not-open-data items render the generic "Public item not found" surface and do not leak private titles. | `src/open-data/OpenDataItemPage.test.tsx`, `tests/smoke/shell.spec.ts` |

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
