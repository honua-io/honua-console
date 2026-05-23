import { describe, expect, it } from "vitest";

import {
  allFilterLabel,
  applyFilter,
  filterFieldForBinding,
  filterLabelForBinding,
  filterOptionsForBinding,
  resolveFilterDefault,
} from "./proofFilter.js";
import type { ProofIncidentRow } from "./proofFixture.js";

const ROWS: readonly ProofIncidentRow[] = [
  {
    id: "INC-A",
    name: "A",
    type: "Water",
    district: "East",
    priority: "High",
    status: "Open",
    coordinates: [-157.8, 21.3],
  },
  {
    id: "INC-B",
    name: "B",
    type: "Road",
    district: "West",
    priority: "Medium",
    status: "Monitoring",
    coordinates: [-157.85, 21.31],
  },
  {
    id: "INC-C",
    name: "C",
    type: "Fire",
    district: "East",
    priority: "Low",
    status: "Contained",
    coordinates: [-157.82, 21.32],
  },
];

describe("studio proof filter helpers", () => {
  it("maps each known binding to the matching row field, or null for unknown", () => {
    expect(filterFieldForBinding("incidents.district")).toBe("district");
    expect(filterFieldForBinding("incidents.priority")).toBe("priority");
    expect(filterFieldForBinding("incidents.status")).toBe("status");
    expect(filterFieldForBinding("incidents.unknown")).toBeNull();
  });

  it("derives a deterministic option list with the All-label first", () => {
    expect(filterOptionsForBinding(ROWS, "incidents.district")).toEqual(["All districts", "East", "West"]);
    expect(filterOptionsForBinding(ROWS, "incidents.priority")).toEqual(["All priorities", "High", "Medium", "Low"]);
    expect(filterOptionsForBinding(ROWS, "incidents.unknown")).toEqual(["All"]);
  });

  it("renders accessible labels for known and unknown bindings", () => {
    expect(filterLabelForBinding("incidents.district")).toBe("District");
    expect(filterLabelForBinding("incidents.priority")).toBe("Priority");
    expect(filterLabelForBinding("incidents.unknown")).toBe("Filter");
  });

  it("returns every All-label that matches a known field", () => {
    expect(allFilterLabel("district")).toBe("All districts");
    expect(allFilterLabel("priority")).toBe("All priorities");
    expect(allFilterLabel("status")).toBe("All statuses");
  });

  it("resolveFilterDefault keeps a stored value when still valid, else falls back", () => {
    const options = ["All districts", "East", "West"];
    expect(resolveFilterDefault("East", options)).toBe("East");
    expect(resolveFilterDefault("All districts", options)).toBe("All districts");
    expect(resolveFilterDefault("Central", options)).toBe("All districts");
    expect(resolveFilterDefault(undefined, options)).toBe("All districts");
    expect(resolveFilterDefault("anything", [])).toBe("All");
  });

  it("applyFilter returns all rows for the matching All-label", () => {
    expect(applyFilter(ROWS, "incidents.district", "All districts")).toEqual(ROWS);
    expect(applyFilter(ROWS, "incidents.priority", "All priorities")).toEqual(ROWS);
  });

  it("applyFilter narrows rows by the matching field value", () => {
    expect(applyFilter(ROWS, "incidents.district", "East").map((row) => row.id)).toEqual(["INC-A", "INC-C"]);
    expect(applyFilter(ROWS, "incidents.priority", "Low").map((row) => row.id)).toEqual(["INC-C"]);
  });

  it("applyFilter passes rows through when the binding is unknown", () => {
    expect(applyFilter(ROWS, "incidents.unknown", "anything")).toEqual(ROWS);
  });

  /**
   * Stale-binding invariant: when the upstream widget binding changes, the
   * previously-selected label can briefly survive in caller state (e.g.
   * "All districts" while the binding is now "incidents.priority"). Callers
   * MUST reconcile through `resolveFilterDefault(...)` against the new
   * options before invoking `applyFilter`; otherwise the dashboard would
   * render zero rows for one frame. This test pins both halves:
   *   - the bare `applyFilter` does narrow with the literal stale value
   *     (documents the contract: it's a pure filter, not a UI reconciler);
   *   - the reconciliation pipeline a caller is expected to run produces
   *     the new All-label and therefore returns every row.
   */
  it("documents the stale-binding reconciliation pipeline callers must run", () => {
    const staleValue = "All districts";
    const nextBinding = "incidents.priority";
    const options = filterOptionsForBinding(ROWS, nextBinding);

    // Without reconciliation the literal stale label filters away everything.
    expect(applyFilter(ROWS, nextBinding, staleValue)).toEqual([]);

    // With reconciliation the caller falls back to the new field's All-label,
    // which `applyFilter` interprets as "no filter".
    const reconciled = options.includes(staleValue) ? staleValue : resolveFilterDefault(undefined, options);
    expect(reconciled).toBe("All priorities");
    expect(applyFilter(ROWS, nextBinding, reconciled)).toEqual(ROWS);
  });
});
