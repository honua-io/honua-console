/**
 * Pure helpers for the Studio proof preview filter. Extracted from
 * `StudioProofPage` so the stale-binding invariant (the previewed filter
 * value must not silently produce an empty row list when the upstream
 * widget binding changes) can be unit-tested in isolation.
 */

import type { ProofIncidentRow } from "./proofFixture.js";

export function filterFieldForBinding(binding: string): keyof ProofIncidentRow | null {
  switch (binding) {
    case "incidents.district":
      return "district";
    case "incidents.priority":
      return "priority";
    case "incidents.status":
      return "status";
    default:
      return null;
  }
}

export function allFilterLabel(field: keyof ProofIncidentRow): string {
  switch (field) {
    case "district":
      return "All districts";
    case "priority":
      return "All priorities";
    case "status":
      return "All statuses";
    default:
      return "All";
  }
}

export function filterLabelForBinding(binding: string): string {
  const field = filterFieldForBinding(binding);
  if (!field) return "Filter";
  return field[0].toUpperCase() + field.slice(1);
}

export function filterOptionsForBinding(rows: readonly ProofIncidentRow[], binding: string): readonly string[] {
  const field = filterFieldForBinding(binding);
  if (!field) return ["All"];
  return [allFilterLabel(field), ...unique(rows.map((row) => String(row[field])))];
}

export function resolveFilterDefault(value: string | undefined, options: readonly string[]): string {
  if (value && options.includes(value)) return value;
  return options[0] ?? "All";
}

export function applyFilter(
  rows: readonly ProofIncidentRow[],
  binding: string,
  value: string,
): readonly ProofIncidentRow[] {
  const field = filterFieldForBinding(binding);
  if (!field || value === allFilterLabel(field)) return rows;
  return rows.filter((row) => String(row[field]) === value);
}

function unique(values: readonly string[]): string[] {
  return [...new Set(values)];
}
