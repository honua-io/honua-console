import { HONUA_MAP_PACKAGE_FORMAT_V1 } from "@honua/sdk-js/runtime";

import type {
  ShareEmbedSettings,
  StudioDependencyRef,
  StudioPackageRef,
  StudioProvenanceRefs,
  StudioPublishDraft,
  StudioPublishTarget
} from "./types.js";

const CREATED_AT = "2026-05-23T22:00:00.000Z";
const WORKSPACE_LAYER_VISIBILITY = "workspace";

const INCIDENT_DEPENDENCY: StudioDependencyRef = {
  itemId: "layer-incidents",
  title: "Incident response layer",
  versionId: "layer-incidents-v7",
  requiredVisibility: WORKSPACE_LAYER_VISIBILITY
};

const FACILITY_DEPENDENCY: StudioDependencyRef = {
  itemId: "layer-facilities",
  title: "Critical facilities layer",
  versionId: "layer-facilities-v3",
  requiredVisibility: WORKSPACE_LAYER_VISIBILITY
};

function packageRef(target: StudioPublishTarget, packageId: string): StudioPackageRef {
  return {
    packageId,
    packageType: `${target}.package`,
    schemaVersion: "1.0.0",
    artifactRef: `artifact://${packageId}`
  };
}

function provenance(target: StudioPublishTarget, packageId: string): StudioProvenanceRefs {
  return {
    promptRef: `prompt://${target}/operations-dashboard`,
    specRef: `spec://${target}/operations-dashboard`,
    planRef: `plan://${target}/operations-dashboard`,
    applyJobRef: `job://apply-${target}-operations`,
    packageArtifactRefs: [
      {
        id: packageId,
        kind: target === "map" ? "map-package" : target === "app" ? "app-package" : "artifact",
        url: `honua://studio/${target}/${packageId}`
      }
    ],
    sourceItemDependencyRefs: [INCIDENT_DEPENDENCY, FACILITY_DEPENDENCY],
    modelRunRefs: ["model-run://studio-fixture-operations-dashboard"],
    actor: "builder@honua.test",
    createdAt: CREATED_AT
  };
}

export const DEFAULT_SHARE_SETTINGS: ShareEmbedSettings = {
  visibility: "private",
  groupIds: [],
  publicLinkEnabled: false,
  embedEnabled: false,
  embedPolicy: "disabled"
};

export const STUDIO_PUBLISH_DRAFTS: readonly StudioPublishDraft[] = [
  {
    draftId: "draft-map-operations",
    target: "map",
    title: "Operations response map",
    summary: "Live response map with incidents, facilities, and district context.",
    tags: ["operations", "response", "map"],
    targetAudience: "Emergency operations builders",
    packageRef: packageRef("map", "map-package-operations-v1"),
    draftPackage: {
      target: "map",
      package: {
        mapPackageId: "map-package-operations-v1",
        format: HONUA_MAP_PACKAGE_FORMAT_V1,
        status: "Ready",
        createdAt: CREATED_AT,
        updatedAt: CREATED_AT,
        sourceBindings: [
          {
            sourceId: "incidents",
            protocol: "workspace_artifact",
            locator: {
              serviceId: INCIDENT_DEPENDENCY.itemId,
              layerId: "incidents-live"
            },
            metadata: {
              title: INCIDENT_DEPENDENCY.title
            }
          },
          {
            sourceId: "facilities",
            protocol: "workspace_artifact",
            locator: {
              serviceId: FACILITY_DEPENDENCY.itemId,
              layerId: "facilities-critical"
            },
            metadata: {
              title: FACILITY_DEPENDENCY.title
            }
          }
        ],
        mapSpec: {
          version: 8,
          sources: {},
          layers: []
        },
        initialView: {
          center: [-157.8583, 21.3069],
          zoom: 11
        },
        legend: [{ label: "Open incidents", color: "#0f766e" }],
        boundArtifacts: ["artifact://map-package-operations-v1"]
      }
    },
    dependencies: [INCIDENT_DEPENDENCY, FACILITY_DEPENDENCY],
    warnings: [
      {
        code: "shared-layer-refresh",
        message: "Incident layer refreshes every 60 seconds; cache policy stays on the source item.",
        severity: "warning"
      }
    ],
    provenance: provenance("map", "map-package-operations-v1")
  },
  {
    draftId: "draft-dashboard-operations",
    target: "dashboard",
    title: "Operations response dashboard",
    summary: "Dashboard with incident counts, trend chart, and map-linked filters.",
    tags: ["operations", "dashboard"],
    targetAudience: "Emergency operations leadership",
    packageRef: packageRef("dashboard", "dashboard-package-operations-v1"),
    draftPackage: {
      target: "dashboard",
      package: {
        id: "dashboard-package-operations-v1",
        schemaVersion: "1.0.0",
        packageType: "dashboard.package",
        title: "Operations response dashboard",
        charts: [
          {
            id: "incidents-by-priority",
            title: "Incidents by priority",
            spec: {
              $schema: "https://vega.github.io/schema/vega-lite/v5.json",
              data: {
                values: [
                  { priority: "High", count: 12 },
                  { priority: "Medium", count: 19 },
                  { priority: "Low", count: 7 }
                ]
              },
              mark: "bar",
              encoding: {
                x: { field: "priority", type: "nominal" },
                y: { field: "count", type: "quantitative" }
              }
            }
          }
        ],
        dataBindings: [INCIDENT_DEPENDENCY.itemId, FACILITY_DEPENDENCY.itemId]
      }
    },
    dependencies: [INCIDENT_DEPENDENCY, FACILITY_DEPENDENCY],
    warnings: [],
    provenance: provenance("dashboard", "dashboard-package-operations-v1")
  },
  {
    draftId: "draft-report-operations",
    target: "report",
    title: "Operations response report",
    summary: "Report draft summarizing response posture and critical facility impacts.",
    tags: ["operations", "report"],
    targetAudience: "Emergency operations leadership",
    packageRef: packageRef("report", "report-package-operations-v1"),
    draftPackage: {
      target: "report",
      package: {
        id: "report-package-operations-v1",
        schemaVersion: "1.0.0",
        packageType: "report.package",
        title: "Operations response report",
        sections: [
          {
            id: "summary",
            title: "Summary",
            body: "Current response status, open incidents, and resource posture."
          },
          {
            id: "facility-impact",
            title: "Facility impact",
            body: "Critical facilities are checked against incident buffers before publication."
          }
        ],
        chartRefs: ["dashboard-package-operations-v1#incidents-by-priority"]
      }
    },
    dependencies: [INCIDENT_DEPENDENCY, FACILITY_DEPENDENCY],
    warnings: [
      {
        code: "report-contract-fixture",
        message: "Report package uses the fixture projection until the shared report package export lands.",
        severity: "info"
      }
    ],
    provenance: provenance("report", "report-package-operations-v1")
  },
  {
    draftId: "draft-app-operations",
    target: "app",
    title: "Operations response app",
    summary: "Generated operations app with map, priority filters, and incident summary cards.",
    tags: ["operations", "generated-app"],
    targetAudience: "Emergency operations builders",
    packageRef: packageRef("app", "app-package-operations-v1"),
    draftPackage: {
      target: "app",
      package: {
        id: "app-package-operations-v1",
        version: "1.0.0",
        assets: [
          {
            id: "app-package-operations-v1",
            kind: "app-package",
            url: "honua://studio/app/app-package-operations-v1"
          }
        ],
        metadata: {
          title: "Operations response app",
          widgets: ["map", "list", "indicator", "chart", "filter"],
          sourceBindings: [INCIDENT_DEPENDENCY.itemId, FACILITY_DEPENDENCY.itemId]
        }
      }
    },
    dependencies: [INCIDENT_DEPENDENCY, FACILITY_DEPENDENCY],
    warnings: [],
    provenance: provenance("app", "app-package-operations-v1"),
    rollbackTargetVersionId: "app-package-operations-v0"
  },
  {
    draftId: "draft-map-conflict",
    target: "map",
    title: "Public incident map draft",
    summary: "Draft intentionally blocked when public sharing would widen private dependencies.",
    tags: ["operations", "map"],
    targetAudience: "Public information",
    packageRef: packageRef("map", "map-package-conflict-v1"),
    draftPackage: {
      target: "map",
      package: {
        mapPackageId: "map-package-conflict-v1",
        format: HONUA_MAP_PACKAGE_FORMAT_V1,
        status: "Ready",
        sourceBindings: [
          {
            sourceId: "incidents",
            protocol: "workspace_artifact",
            locator: {
              serviceId: "layer-private-incidents",
              layerId: "incidents-private"
            },
            metadata: {
              title: "Private incident layer"
            }
          }
        ],
        mapSpec: {
          version: 8,
          sources: {},
          layers: []
        }
      }
    },
    dependencies: [
      {
        itemId: "layer-private-incidents",
        title: "Private incident layer",
        versionId: "layer-private-incidents-v1",
        requiredVisibility: "private"
      }
    ],
    warnings: [
      {
        code: "dependency-private",
        message: "Private incident layer requires private visibility; wider sharing will be blocked.",
        severity: "warning"
      }
    ],
    provenance: provenance("map", "map-package-conflict-v1")
  }
];
