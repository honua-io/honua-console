import type { Page } from '@playwright/test';

export interface PopulatedConsoleApiOverrides {
  service?: Record<string, unknown>;
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

export async function mockPopulatedConsoleApis(
  page: Page,
  origin: string,
  overrides: PopulatedConsoleApiOverrides = {},
) {
  const service = defaultService(overrides.service);
  const proposal = defaultProposal(overrides.proposal);
  const opsHealth = defaultOpsHealth(overrides.opsHealth);

  await page.route(`${origin}/api/v1/admin/services**`, route => route.fulfill({
    json: { success: true, data: { items: [service], page: 1, pageSize: 100, total: 1, layerTotal: service.layerCount } },
  }));
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
