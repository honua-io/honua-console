/**
 * Loader for the golden catalog fixtures shipped under `fixtures/catalog/`.
 *
 * The fixtures are the same set every consumer repo MUST roundtrip, so the
 * portal's fixture client is just one consumer of the shared corpus. The
 * loader picks IDs deterministically and marks the unauthorized/unsupported
 * branches so dependency walker tests can prove every empty/error surface.
 */

import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import type { ContentItem } from "../contracts/content-item.js";
import type { FixtureCatalogData } from "./client.js";

const HERE = dirname(fileURLToPath(import.meta.url));
const FIXTURES_ROOT = resolve(HERE, "..", "..", "fixtures", "catalog");

export const FIXTURE_FILES = [
  "service.json",
  "layer.json",
  "tile-service.json",
  "map.json",
  "scene.json",
  "app.json",
  "document.json",
  "external-url.json",
  "deps-fanout.json",
  "unsupported.json",
  "unauthorized.json",
  "service-no-docs.json",
  "service-honua-api.json",
] as const;

export const UNAUTHORIZED_FIXTURE_IDS = ["01HXY3ZK7N1J2Q9V8M0FQ2PWAN"] as const;
export const UNSUPPORTED_FIXTURE_IDS = ["01HXY3ZK7N1J2Q9V8M0FQ2PWAM"] as const;
export const MISSING_FIXTURE_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PW00";

export function fixturePath(name: string): string {
  return resolve(FIXTURES_ROOT, name);
}

export function readFixture<T = unknown>(name: string): T {
  const raw = readFileSync(fixturePath(name), "utf8");
  return JSON.parse(raw) as T;
}

export function loadCatalogFixtures(): FixtureCatalogData {
  const items = new Map<string, ContentItem>();
  const listOrder: string[] = [];
  const unauthorizedIds = new Set<string>(UNAUTHORIZED_FIXTURE_IDS);
  for (const file of FIXTURE_FILES) {
    const item = readFixture<ContentItem>(file);
    items.set(item.id, item);
    // Exclude the deps-fanout aggregate (used for walker tests, not as a
    // listable card) and any unauthorized id (the list endpoint MUST mirror
    // the golden list-response.json which omits inaccessible items).
    if (file === "deps-fanout.json") continue;
    if (unauthorizedIds.has(item.id)) continue;
    listOrder.push(item.id);
  }
  return {
    items,
    listOrder,
    unauthorizedIds,
    unsupportedIds: new Set(UNSUPPORTED_FIXTURE_IDS),
  };
}
