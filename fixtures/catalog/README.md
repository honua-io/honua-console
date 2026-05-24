# Catalog Golden Fixtures

These fixtures are the **golden** representation of `content-item/v1`. Every consumer repo (`honua-portal`, `honua-server`, `honua-server-admin`, `honua-sdk-js`) MUST roundtrip them in its CI to prove contract parity.

## Layout

| File | Purpose |
| --- | --- |
| `service.json` | Item type `service` (Feature service). |
| `layer.json` | Item type `layer`. Depends on `service.json`. |
| `map.json` | Item type `map` (operational layer references `layer.json`, basemap references `tile-service.json`). |
| `tile-service.json` | Secondary `service` used as a basemap dependency target. |
| `scene.json` | Item type `scene`. |
| `app.json` | Item type `app` (framework `honua`). |
| `document.json` | Item type `document`. |
| `external-url.json` | Item type `external-url`. |
| `deps-fanout.json` | A `map` whose dependency closure is 3 levels deep with one missing, one unauthorized, and one unsupported edge. |
| `unsupported.json` | A `service` whose `target.kind` is renderable but the portal viewer does not yet support its protocol — used to exercise the `unsupported` empty surface. |
| `unauthorized.json` | An item the catalog will return as `403` when fetched, used to exercise `unauthorized`. |
| `empty.json` | List response shape with zero items. |
| `list-response.json` | Full list response envelope including all listable items above. |
| `publish-handoff.json` | A valid `publish-handoff-v1` payload used by parity tests and admin/server child tickets. |

## Stable IDs

The IDs used here are deliberately deterministic so the walker tests can reference them by constant.

| ID | File |
| --- | --- |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAB` | `service.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAC` | `layer.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAD` | `map.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAE` | `tile-service.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAF` | `scene.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAG` | `app.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAH` | `document.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAJ` | `external-url.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAK` | `deps-fanout.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAM` | `unsupported.json` |
| `01HXY3ZK7N1J2Q9V8M0FQ2PWAN` | `unauthorized.json` |

`01HXY3ZK7N1J2Q9V8M0FQ2PW00` is reserved for "missing" — no fixture exists at that ID. The walker tests use it to assert the `missing` surface.
