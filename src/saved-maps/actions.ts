/**
 * High-level user actions for saved maps. These are the seams the viewer
 * (#13) calls into when the user clicks Save / Save As / Rename / Delete.
 *
 * Composing serializer + client + thumbnail capture here keeps the viewer
 * UI bindings dumb and gives us a single place to enforce ordering rules
 * (e.g. thumbnail capture must not block save on failure).
 */

import type { SavedMapClient } from "./client.js";
import { type CaptureThumbnailOptions, type MapHandle, captureThumbnail } from "./thumbnail.js";
import type { DuplicateMapInput, RenameMapInput, SaveMapInput, SavedMapItem, ViewerState } from "./types.js";

export interface SaveMapWithThumbnailInput extends Omit<SaveMapInput, "thumbnail"> {
  /** Active map handle for thumbnail capture. Optional. */
  map?: MapHandle | null;
  thumbnailOptions?: CaptureThumbnailOptions;
}

export interface SaveMapResult {
  item: SavedMapItem;
  thumbnailUploaded: boolean;
  thumbnailWarning?: string;
}

/**
 * Save flow:
 *   1. Serialize viewer state and POST item (no thumbnail yet).
 *   2. In parallel, attempt thumbnail capture.
 *   3. If the thumbnail succeeds, upload + PATCH the item with the URL.
 *   4. If the thumbnail fails, log a warning and return the item unchanged
 *      (preview.thumbnail stays null).
 *
 * Step 3 is intentionally a follow-up PATCH rather than a multipart create,
 * so a thumbnail failure never aborts the save.
 */
export async function saveMap(client: SavedMapClient, input: SaveMapWithThumbnailInput): Promise<SaveMapResult> {
  const { map, thumbnailOptions, ...rest } = input;
  const [item, thumbnailResult] = await Promise.all([
    client.create({ ...rest, thumbnail: null }),
    map ? captureThumbnail(map, thumbnailOptions) : Promise.resolve(null),
  ]);

  if (!thumbnailResult || !thumbnailResult.ok) {
    return {
      item,
      thumbnailUploaded: false,
      ...(thumbnailResult && !thumbnailResult.ok ? { thumbnailWarning: thumbnailResult.reason } : {}),
    };
  }

  try {
    await client.uploadThumbnail(item.id, thumbnailResult.blob);
    const refreshed = await client.get(item.id);
    return { item: refreshed ?? item, thumbnailUploaded: true };
  } catch (error) {
    return {
      item,
      thumbnailUploaded: false,
      thumbnailWarning: errorMessage(error),
    };
  }
}

export async function duplicateMap(client: SavedMapClient, input: DuplicateMapInput): Promise<SavedMapItem> {
  return await client.duplicate(input);
}

export async function renameMap(client: SavedMapClient, input: RenameMapInput): Promise<SavedMapItem> {
  return await client.patchMetadata(input);
}

export async function replaceMapContent(client: SavedMapClient, id: string, state: ViewerState): Promise<SavedMapItem> {
  const { viewerStateToWebMapDoc } = await import("./serializer.js");
  return await client.replaceContent(id, viewerStateToWebMapDoc(state));
}

export async function deleteMap(client: SavedMapClient, id: string): Promise<void> {
  await client.delete(id);
}

function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  return String(error);
}
