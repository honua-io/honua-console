import type { ClosureItem, ShareAccess } from "../types.js";

const access = (sharing: ShareAccess["sharing"], embeddable = false): ShareAccess => ({ sharing, embeddable });

/**
 * Standard fixture graph: a saved map (`map-1`) with two operational
 * layers, two services, and one shared style. Used across the share,
 * closure, and embed-permission tests so the assertions stay readable.
 *
 *   map-1 (private/embed=false)
 *   ├── layer-a (org)         service-a (org)
 *   ├── layer-b (private)  ── service-b (private)
 *   └── style-1 (unsupported)
 *
 * Owner id `user-1` owns map-1, layer-a, and layer-b. `user-2` owns the
 * services. Used as the editable-set in the client tests.
 */
export const FIXTURE_ITEMS: ClosureItem[] = [
  {
    id: "map-clean",
    type: "map",
    title: "Clean saved map",
    access: access("private"),
    dependencies: [{ id: "service-a", type: "service", role: "operationalLayer" }],
  },
  {
    id: "map-1",
    type: "map",
    title: "Pilot saved map",
    access: access("private"),
    dependencies: [
      { id: "layer-a", type: "layer", role: "operationalLayer" },
      { id: "layer-b", type: "layer", role: "operationalLayer" },
      { id: "style-1", type: "style", role: "style" },
    ],
  },
  {
    id: "layer-a",
    type: "layer",
    title: "Layer A",
    access: access("org"),
    dependencies: [{ id: "service-a", type: "service", role: "operationalLayer" }],
  },
  {
    id: "layer-b",
    type: "layer",
    title: "Layer B",
    access: access("private"),
    dependencies: [{ id: "service-b", type: "service", role: "operationalLayer" }],
  },
  {
    id: "service-a",
    type: "service",
    title: "Service A",
    access: access("org"),
    dependencies: [],
  },
  {
    id: "service-b",
    type: "service",
    title: "Service B",
    access: access("private"),
    dependencies: [],
  },
  {
    id: "style-1",
    type: "style",
    title: "Shared style",
    access: "unsupported",
    dependencies: [],
  },
];

export const FIXTURE_OWNER_OF = new Map([
  ["map-clean", "user-1"],
  ["map-1", "user-1"],
  ["layer-a", "user-1"],
  ["layer-b", "user-1"],
  ["service-a", "user-2"],
  ["service-b", "user-2"],
  ["style-1", "user-1"],
]);
