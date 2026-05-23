import type { AppPackage } from "@honua/sdk-js/operator";

import mapItem from "../../../fixtures/catalog/proof-source-map.json";
import type { Session } from "../../auth/types.js";
import type { ContentItem, Owner } from "../../transitional/content-item.js";
import {
  FixtureGeneratedAppLifecycleClient,
  type GeneratedAppLifecycleClient,
  HttpGeneratedAppLifecycleClient,
} from "./client.js";
import { addGeneratedAppRevision, materializeGeneratedAppDraft, publishGeneratedAppItem } from "./lifecycle.js";
import type { GeneratedAppLifecycleRecord, GeneratedAppRevisionInput } from "./types.js";

export const DEFAULT_GENERATED_APP_ITEM_ID = "01J7APPS00000000000000";
export const DEFAULT_GENERATED_APP_CONSOLE_BASE_URL = "https://console.honua.example";
export const DEFAULT_GENERATED_APP_API_BASE_URL = "/api/v1/console";

const DEFAULT_OWNER: Owner = {
  id: "u-member",
  name: "Mira Chen",
  kind: "user",
};

const SOURCE_MAP = mapItem as unknown as ContentItem;

export function getDefaultGeneratedAppLifecycleClient(session?: Session): GeneratedAppLifecycleClient {
  if (shouldUseFixtureLifecycleClient()) {
    return getDefaultFixtureGeneratedAppLifecycleClient(session);
  }
  return new HttpGeneratedAppLifecycleClient({
    baseUrl: getGeneratedAppLifecycleApiBaseUrl(),
    session,
  });
}

function getDefaultFixtureGeneratedAppLifecycleClient(session?: Session): FixtureGeneratedAppLifecycleClient {
  return new FixtureGeneratedAppLifecycleClient({
    consoleBaseUrl: DEFAULT_GENERATED_APP_CONSOLE_BASE_URL,
    actorId: session?.status === "authenticated" ? session.user.id : null,
    records: buildDefaultGeneratedAppLifecycleRecords(),
  });
}

function shouldUseFixtureLifecycleClient(): boolean {
  const mode = (import.meta.env.VITE_GENERATED_APP_LIFECYCLE_CLIENT as string | undefined)?.toLowerCase() ?? "auto";
  if (mode === "fixture") return true;
  if (mode === "http") return false;
  const sessionDriver = (import.meta.env.VITE_SESSION_DRIVER as string | undefined)?.toLowerCase() ?? "fixture";
  return sessionDriver === "fixture";
}

function getGeneratedAppLifecycleApiBaseUrl(): string {
  return (
    (import.meta.env.VITE_CONSOLE_API_BASE_URL as string | undefined)?.trim() || DEFAULT_GENERATED_APP_API_BASE_URL
  );
}

export function buildDefaultGeneratedAppLifecycleRecords(): readonly GeneratedAppLifecycleRecord[] {
  const draft = materializeGeneratedAppDraft(
    {
      id: DEFAULT_GENERATED_APP_ITEM_ID,
      slug: "operations-dashboard-proof",
      title: "Operations dashboard proof",
      summary: "Generated AppPackage operations dashboard for the GTM proof path.",
      description:
        "Private generated app item produced by the deterministic operations-dashboard proof. It stores package, manifest, plan, source-map, and server job references so the app can reopen without regenerating.",
      tags: ["generated-app", "operations", "proof"],
      owner: DEFAULT_OWNER,
      source: { kind: "saved-map", item: SOURCE_MAP },
      actor: DEFAULT_OWNER.id,
      manifestVersion: "honua-generated-app-manifest/v1",
      buildSpecRef: artifact(
        "buildspec-ops-dashboard-v1",
        "artifact",
        "https://api.honua.example/artifacts/buildspec-v1.json",
      ),
      plan: { id: "plan-ops-dashboard-v1", warnings: ["deterministic fixture plan"] },
      planArtifact: artifact("plan-ops-dashboard-v1", "artifact", "https://api.honua.example/artifacts/plan-v1.json"),
      appPackage: appPackage("app-package-ops-dashboard-v1", "1.0.0"),
      manifestArtifact: artifact(
        "manifest-ops-dashboard-v1",
        "artifact",
        "https://api.honua.example/artifacts/app-manifest-v1.json",
      ),
      serverJob: {
        id: "job-ops-dashboard-v1",
        status: "succeeded",
        url: "https://api.honua.example/jobs/job-ops-dashboard-v1",
      },
      provenance: [{ step: "builder.apply", tool: "fixture", startedAt: 1770000000000, finishedAt: 1770000005000 }],
    },
    {
      consoleBaseUrl: DEFAULT_GENERATED_APP_CONSOLE_BASE_URL,
      now: "2026-05-08T17:00:00.000Z",
      revisionId: "rev-001",
    },
  );

  const withSecondRevision = addGeneratedAppRevision(
    draft.item,
    revisionInput("v2", "2.0.0", "revise district filter default"),
    {
      consoleBaseUrl: DEFAULT_GENERATED_APP_CONSOLE_BASE_URL,
      actor: DEFAULT_OWNER.id,
      now: "2026-05-08T17:15:00.000Z",
      revisionId: "rev-002",
    },
  );

  const published = publishGeneratedAppItem(withSecondRevision.item, {
    consoleBaseUrl: DEFAULT_GENERATED_APP_CONSOLE_BASE_URL,
    actor: DEFAULT_OWNER.id,
    now: "2026-05-08T17:20:00.000Z",
  });

  return [published];
}

function revisionInput(label: string, version: string, warning: string): GeneratedAppRevisionInput {
  return {
    actor: DEFAULT_OWNER.id,
    label,
    manifestVersion: "honua-generated-app-manifest/v1",
    buildSpecRef: artifact(
      `buildspec-ops-dashboard-${version}`,
      "artifact",
      `https://api.honua.example/artifacts/buildspec-${version}.json`,
    ),
    plan: { id: `plan-ops-dashboard-${version}`, warnings: [warning] },
    planArtifact: artifact(
      `plan-ops-dashboard-${version}`,
      "artifact",
      `https://api.honua.example/artifacts/plan-${version}.json`,
    ),
    appPackage: appPackage(`app-package-ops-dashboard-${version}`, version),
    manifestArtifact: artifact(
      `manifest-ops-dashboard-${version}`,
      "artifact",
      `https://api.honua.example/artifacts/app-manifest-${version}.json`,
    ),
    serverJob: {
      id: `job-ops-dashboard-${version}`,
      status: "succeeded",
      url: `https://api.honua.example/jobs/job-ops-dashboard-${version}`,
    },
    provenance: [{ step: "builder.refine", tool: "fixture", startedAt: 1770000900000, finishedAt: 1770000904000 }],
  };
}

function appPackage(id: string, version: string): AppPackage {
  return {
    id,
    version,
    assets: [
      {
        id,
        kind: "app-package",
        url: `https://api.honua.example/packages/${id}.json`,
      },
    ],
    metadata: {
      title: "Operations dashboard proof",
      manifestVersion: "honua-generated-app-manifest/v1",
    },
  };
}

function artifact(id: string, kind: "app-package" | "artifact", url: string) {
  return { id, kind, url };
}
