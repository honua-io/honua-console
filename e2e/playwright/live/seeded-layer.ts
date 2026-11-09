import { expect } from '@playwright/test';
import type { AdminApi } from './admin-api';
import { sourceConnectionBody } from './source-db';
import { readAdminItems } from './admin-items.mjs';

export interface LayerSeed {
  /** Kept (untracked) connection the layer is published from; fixed so a retry finds it again. */
  readonly connectionName: string;
  /** `schema.table` no other spec publishes: honua-server backs a source table with one layer only. */
  readonly table: string;
  readonly layerName: string;
  readonly serviceName: string;
}

export interface SeededLayer {
  readonly layerId: number;
  readonly layerName: string;
  readonly serviceName: string;
}

// Publishes the seed table once per testbed. A retry (or a later test in the same file) gets the
// layer that is already there, under the name and service it was first published with.
export async function ensurePublishedLayer(admin: AdminApi, seed: LayerSeed): Promise<SeededLayer> {
  const connection = await admin.getOrCreateKeptConnection(sourceConnectionBody(seed.connectionName));
  await waitForTable(admin, connection.connectionId, seed.table);
  const existing = await findLayerForTable(admin, connection.connectionId, seed.table);
  if (existing) return existing;

  const [schema, table] = seed.table.split('.');
  return admin.publishLayer(connection.connectionId, {
    schema,
    table,
    layerName: seed.layerName,
    serviceName: seed.serviceName,
  });
}

async function waitForTable(admin: AdminApi, connectionId: string, qualifiedTable: string): Promise<void> {
  await expect
    .poll(async () => {
      const tables = await admin.getJson(`/api/v1/admin/connections/${connectionId}/tables`);
      return (tables.tables ?? []).some((t: any) => `${t.schema}.${t.table}` === qualifiedTable);
    }, { message: `${qualifiedTable} is discoverable`, timeout: 90_000, intervals: [1000, 2000, 5000] })
    .toBeTruthy();
}

// The connection's layer listing is scoped by service, so look in every service the server reports.
async function findLayerForTable(admin: AdminApi, connectionId: string, qualifiedTable: string): Promise<SeededLayer | undefined> {
  const services: { serviceName: string }[] = await readAdminItems(path => admin.getJson(path), '/api/v1/admin/services');
  for (const { serviceName } of services) {
    const layers = await admin.listLayers(connectionId, serviceName);
    const layer = layers.find((l) => `${l.schema}.${l.table}` === qualifiedTable);
    if (layer) return { layerId: layer.layerId, layerName: layer.layerName, serviceName: layer.serviceName ?? serviceName };
  }
  return undefined;
}
