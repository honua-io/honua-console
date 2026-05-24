import type { ReactElement, ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";

import { CatalogClientProvider } from "../catalog/CatalogContext.js";
import { FixtureCatalogClient } from "../catalog/client.js";
import type { CatalogClient } from "../catalog/client.js";
import { loadCatalogFixtures } from "../catalog/fixtures.js";

export interface HarnessOptions {
  readonly initialEntries?: string[];
  readonly client?: CatalogClient;
}

export function harness(node: ReactElement, options: HarnessOptions = {}): ReactElement {
  const client = options.client ?? new FixtureCatalogClient(loadCatalogFixtures());
  return wrap(client, node, options.initialEntries ?? ["/catalog"]);
}

function wrap(client: CatalogClient, node: ReactNode, initialEntries: string[]): ReactElement {
  return (
    <CatalogClientProvider client={client}>
      <MemoryRouter initialEntries={initialEntries}>{node}</MemoryRouter>
    </CatalogClientProvider>
  );
}

export function makeFixtureClient(): CatalogClient {
  return new FixtureCatalogClient(loadCatalogFixtures());
}
