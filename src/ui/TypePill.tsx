import type { ItemType } from "../contracts/content-item.js";
import { Pill } from "./Pill.js";

const LABELS: Record<ItemType, string> = {
  service: "Service",
  layer: "Layer",
  map: "Map",
  scene: "Scene",
  app: "App",
  document: "Document",
  "external-url": "External link",
};

export function typeLabel(type: ItemType): string {
  return LABELS[type];
}

export interface TypePillProps {
  readonly type: ItemType;
}

export function TypePill({ type }: TypePillProps) {
  return (
    <Pill tone="muted" title={`Item type: ${LABELS[type]}`}>
      {LABELS[type]}
    </Pill>
  );
}
