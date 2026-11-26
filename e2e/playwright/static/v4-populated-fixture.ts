import type { Page } from '@playwright/test';

export interface PopulatedConsoleApiOverrides {
  service?: Record<string, unknown>;
  connection?: Record<string, unknown>;
  layer?: Record<string, unknown>;
  catalogRegistry?: Record<string, unknown>;
  proposal?: Record<string, unknown>;
  opsHealth?: Record<string, unknown>;
  apiKeys?: Record<string, unknown>[];
  exports?: Record<string, unknown>[];
  openDataItems?: Record<string, unknown>[];
}

function defaultService(overrides?: Record<string, unknown>) {
  return {
    serviceName: 'transport-roads',
    folderId: null,
    enabledProtocols: ['FeatureServer', 'OgcFeatures'],
    layerCount: 2,
    allowAnonymous: false,
    ...overrides,
  };
}

function defaultConnection(overrides?: Record<string, unknown>) {
  return {
    connectionId: '11111111-1111-4111-8111-111111111111',
    name: 'Primary PostGIS',
    provider: 'PostgreSQL/PostGIS',
    host: 'db.example.internal',
    port: 5432,
    databaseName: 'gis_prod',
    username: 'honua_reader',
    isActive: true,
    healthStatus: 'Healthy',
    ...overrides,
  };
}

function defaultLayer(serviceName: string, overrides?: Record<string, unknown>) {
  return {
    storageLayerId: 1,
    layerId: 1,
    layerName: 'Road Centerlines',
    schema: 'public',
    table: 'road_centerlines',
    description: 'Authoritative regional road centerlines',
    geometryType: 'LineString',
    srid: 4326,
    primaryKey: 'id',
    fieldCount: 6,
    enabled: true,
    serviceName,
    ...overrides,
  };
}

function defaultCatalogRegistry(overrides?: Record<string, unknown>) {
  return {
    environmentId: 'current',
    environmentName: 'Production',
    publicHost: 'https://gis.example.org',
    autoDefaultCount: 1,
    optInCount: 1,
    endpoints: [
      {
        key: 'ogc-records',
        title: 'OGC API Records',
        description: 'Standards-based discovery catalog',
        dialect: 'ogc',
        enabled: true,
        autoDefault: true,
        url: 'https://gis.example.org/ogc/records',
        entries: 12,
        fedBy: 'Published services',
        feeders: [{ kind: 'services', label: 'Published services' }],
        issueCount: 0,
      },
      {
        key: 'stac',
        title: 'STAC Catalog',
        description: 'SpatioTemporal Asset Catalog',
        dialect: 'stac',
        enabled: false,
        autoDefault: false,
        url: 'https://gis.example.org/stac',
        entries: 0,
        fedBy: 'Exports',
        feeders: [{ kind: 'exports', label: 'Exports' }],
        issueCount: 0,
      },
    ],
    ...overrides,
  };
}

function defaultProposal(overrides?: Record<string, unknown>) {
  return {
    proposalId: 'prop-a11y-1',
    kind: 'deploy',
    status: 'awaiting_approval',
    riskLevel: 'medium',
    summary: 'Promote vector tile cache settings for roads',
    requestedBy: 'ops-agent',
    createdAt: '2026-11-24T01:00:00Z',
    ...overrides,
  };
}

function defaultOpsHealth(overrides?: Record<string, unknown>) {
  return {
    overallStatus: 'healthy',
    generatedAt: '2026-11-24T01:05:00Z',
    health: {
      status: 'healthy',
      totalDurationMs: 12.4,
      entries: [{ name: 'postgres', status: 'healthy', durationMs: 4.1, description: 'Connected' }],
    },
    servingLatency: {
      windowSeconds: 300,
      protocols: [{ protocol: 'OgcFeatures', requestCount: 120, errorCount: 0, errorRate: 0, p50Ms: 12, p95Ms: 34, p99Ms: 52, maxMs: 70 }],
    },
    geoprocessing: { available: true, totalActive: 0, buckets: [] },
    alertDispatch: { dispatcherEnabled: true, dispatcherRunning: true, storagePollFailing: false, pendingCount: 0, deadLetteredCount: 0 },
    deploy: { readyForCoordinatedDeploy: true, pendingMigrationsCount: 0, pendingContractScriptsCount: 0 },
    database: {
      hasConnectionPoolData: true,
      connectionPoolUtilization: 0.25,
      cacheHitRatio: 0.96,
      errorRate: 0,
      activeConnections: 4,
      connectionAcquisitionTimeouts: 0,
      connectionAcquisitionFailures: 0,
    },
    managedTargets: [],
    ...overrides,
  };
}

async function mockAdminCatalogRoutes(
  page: Page,
  origin: string,
  overrides: PopulatedConsoleApiOverrides,
) {
  const service = defaultService(overrides.service);
  const connection = defaultConnection(overrides.connection);
  const layer = defaultLayer(String(service.serviceName), overrides.layer);
  const catalogRegistry = defaultCatalogRegistry(overrides.catalogRegistry);

  await page.route(url => url.origin === origin && /^\/api\/v1\/admin\/connections\/?$/.test(url.pathname), route => route.fulfill({
    json: { success: true, data: [connection] },
  }));
  await page.route(url => url.origin === origin && /^\/api\/v1\/admin\/connections\/[^/]+\/layers\/?$/.test(url.pathname), route => route.fulfill({
    json: { success: true, data: { items: [layer], page: 1, pageSize: 100, total: 1 } },
  }));
  await page.route(`${origin}/api/v1/console/catalog-endpoints/**`, route => route.fulfill({
    json: { success: true, data: catalogRegistry },
  }));
  await page.route(`${origin}/api/v1/admin/services**`, route => route.fulfill({
    json: { success: true, data: { items: [service], page: 1, pageSize: 100, total: 1, layerTotal: service.layerCount } },
  }));
}

export async function mockPopulatedConsoleApis(
  page: Page,
  origin: string,
  overrides: PopulatedConsoleApiOverrides = {},
) {
  const proposal = defaultProposal(overrides.proposal);
  const opsHealth = defaultOpsHealth(overrides.opsHealth);

  await mockAdminCatalogRoutes(page, origin, overrides);
  await page.route(`${origin}/api/v1/admin/proposals**`, route => route.fulfill({
    json: { proposals: [proposal] },
  }));
  await page.route(`${origin}/api/v1/admin/observability/ops-health`, route => route.fulfill({
    json: opsHealth,
  }));
  await page.route(`${origin}/api/v1/admin/observability/ops-health/history**`, route => route.fulfill({
    json: { from: '2026-11-24T00:05:00Z', to: '2026-11-24T01:05:00Z', resolution: '1m', perReplica: false, latencySeries: [], vitalsPoints: [] },
  }));
  if (overrides.apiKeys) {
    await page.route(`${origin}/api/v1/admin/api-keys**`, route => route.fulfill({
      json: { success: true, data: overrides.apiKeys },
    }));
  }
  if (overrides.exports) {
    await page.route(`${origin}/api/v1/admin/exports**`, route => route.fulfill({
      json: { success: true, data: { items: overrides.exports } },
    }));
  }
  if (overrides.openDataItems) {
    await page.route(`${origin}/api/v1/open-data/datasets**`, route => route.fulfill({
      json: { success: true, data: { items: overrides.openDataItems } },
    }));
  }
}
