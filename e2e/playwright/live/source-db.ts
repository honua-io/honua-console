// The source datasource the live specs publish from.
//
// These specs create a data connection on the live honua-server and publish a layer out of it, so
// the host/port/credentials they type into the connection form must be resolvable FROM INSIDE the
// server, not from the Playwright process. Locally that is the console-testbed compose stack
// (e2e/docker-compose.yml), where honua-server shares PostGIS's network namespace so `localhost:5544`
// means PostGIS on both sides.
//
// Other harnesses boot a different topology — honua-release's Slice-1 candidate stack, for one, runs
// PostGIS as a separate compose service the server reaches at `db:5432` — and the suite used to
// hardcode the testbed's DSN, so it could only ever pass against the testbed. Every value is now
// overridable; the defaults are the testbed's, so `npm run e2e:live` behaves exactly as before.
//
// The source table must match e2e/initdb/01-seed.sql: an integer-PK polygon table in EPSG:3857 with
// exactly 3 features (services-layers asserts the published layer serves all three back).
export const SOURCE_DB = {
  host: process.env.HONUA_CONSOLE_E2E_SOURCE_HOST ?? 'localhost',
  port: process.env.HONUA_CONSOLE_E2E_SOURCE_PORT ?? '5544',
  database: process.env.HONUA_CONSOLE_E2E_SOURCE_DB ?? 'honua_dev',
  username: process.env.HONUA_CONSOLE_E2E_SOURCE_USER ?? 'honua_user',
  password: process.env.HONUA_CONSOLE_E2E_SOURCE_PASSWORD ?? 'honua_password',
  table: process.env.HONUA_CONSOLE_E2E_SOURCE_TABLE ?? 'public.e2e_layer_src',
} as const;

/** The connection body shape the admin API expects for the source datasource. */
export function sourceConnectionBody(name: string): Record<string, unknown> {
  return {
    name,
    host: SOURCE_DB.host,
    port: Number(SOURCE_DB.port),
    databaseName: SOURCE_DB.database,
    username: SOURCE_DB.username,
    password: SOURCE_DB.password,
    provider: 'postgis',
    sslRequired: false,
    sslMode: 'Disable',
  };
}
