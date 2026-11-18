// honua-server names the publishing DTO's identifier storageLayerId; servers before 8 Nov
// sent layerId. The specs read layerId either way.
export function withLayerId<T extends { storageLayerId?: number; layerId?: number }>(layer: T): T & { layerId?: number } {
  return { ...layer, layerId: layer.storageLayerId ?? layer.layerId };
}
