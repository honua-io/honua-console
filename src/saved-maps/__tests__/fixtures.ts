import { createDeterministicContentItemIdGenerator } from "../../contracts/ids.js";
import type { ViewerState } from "../types.js";

export const TEST_CENSUS_LAYER_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAC";
export const TEST_SCHOOLS_LAYER_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWR2";
export const TEST_CENSUS_STYLE_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAH";
export const TEST_BASEMAP_SERVICE_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAE";

export function makeViewerState(overrides: Partial<ViewerState> = {}): ViewerState {
  return {
    operationalLayers: [
      {
        id: "ol-1",
        title: "Census tracts",
        layerType: "honua-feature",
        sourceRef: { itemId: TEST_CENSUS_LAYER_ID },
        styleRef: { itemId: TEST_CENSUS_STYLE_ID },
        visibility: true,
        opacity: 0.9,
        popupInfo: { title: "{NAME}" },
        minScale: null,
        maxScale: null,
      },
      {
        id: "ol-2",
        title: "School districts",
        layerType: "honua-feature",
        sourceRef: { itemId: TEST_SCHOOLS_LAYER_ID },
        styleRef: null,
        visibility: false,
        opacity: 1,
        popupInfo: null,
        minScale: null,
        maxScale: null,
      },
    ],
    baseMap: {
      title: "Honua Streets",
      baseMapLayers: [
        {
          id: "bm-1",
          title: "Streets",
          layerType: "honua-vector-tile",
          sourceRef: { itemId: TEST_BASEMAP_SERVICE_ID },
          visibility: true,
          opacity: 1,
        },
      ],
    },
    extent: {
      xmin: -122.6,
      ymin: 37.6,
      xmax: -122.3,
      ymax: 37.9,
      rotation: 0,
    },
    ...overrides,
  };
}

export function deterministicNow(start = "2026-05-06T12:00:00.000Z"): () => Date {
  let ms = new Date(start).getTime();
  return () => {
    const date = new Date(ms);
    ms += 1; // monotonic
    return date;
  };
}

export function deterministicIdGenerator(prefix = "id"): () => string {
  return createDeterministicContentItemIdGenerator(prefix);
}
