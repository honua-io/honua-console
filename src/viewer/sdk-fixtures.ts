const CITY_PARCELS_FEATURES = [
  {
    attributes: {
      OBJECTID: 1,
      PARCEL_ID: "HON-001",
      LAND_USE: "Residential",
      OWNER_TYPE: "Private",
      ASSESSED_VALUE: 720000,
    },
    geometry: {
      rings: [
        [
          [-157.848, 21.306],
          [-157.842, 21.306],
          [-157.842, 21.312],
          [-157.848, 21.312],
          [-157.848, 21.306],
        ],
      ],
    },
  },
  {
    attributes: {
      OBJECTID: 2,
      PARCEL_ID: "HON-002",
      LAND_USE: "Civic",
      OWNER_TYPE: "Municipal",
      ASSESSED_VALUE: 1380000,
    },
    geometry: {
      rings: [
        [
          [-157.837, 21.315],
          [-157.83, 21.315],
          [-157.83, 21.322],
          [-157.837, 21.322],
          [-157.837, 21.315],
        ],
      ],
    },
  },
  {
    attributes: {
      OBJECTID: 3,
      PARCEL_ID: "HON-003",
      LAND_USE: "Commercial",
      OWNER_TYPE: "Private",
      ASSESSED_VALUE: 2115000,
    },
    geometry: {
      rings: [
        [
          [-157.86, 21.294],
          [-157.852, 21.294],
          [-157.852, 21.301],
          [-157.86, 21.301],
          [-157.86, 21.294],
        ],
      ],
    },
  },
] as const;

const CITY_PARCELS_QUERY_RESPONSE = {
  objectIdFieldName: "OBJECTID",
  geometryType: "esriGeometryPolygon",
  spatialReference: { wkid: 4326 },
  fields: [
    { name: "OBJECTID", type: "esriFieldTypeOID", alias: "OBJECTID" },
    { name: "PARCEL_ID", type: "esriFieldTypeString", alias: "Parcel ID" },
    { name: "LAND_USE", type: "esriFieldTypeString", alias: "Land use" },
    { name: "OWNER_TYPE", type: "esriFieldTypeString", alias: "Owner type" },
    { name: "ASSESSED_VALUE", type: "esriFieldTypeDouble", alias: "Assessed value" },
  ],
  features: CITY_PARCELS_FEATURES,
  exceededTransferLimit: false,
};

export function createFixturePortalViewerSdkFetch(fallback?: typeof fetch): typeof fetch {
  return async (input, init) => {
    if (isCityParcelsQuery(input)) {
      return new Response(JSON.stringify(CITY_PARCELS_QUERY_RESPONSE), {
        headers: { "Content-Type": "application/json" },
        status: 200,
      });
    }

    if (fallback) return fallback(input, init);
    throw new Error(`No fixture SDK response for ${String(input)}`);
  };
}

function isCityParcelsQuery(input: Parameters<typeof fetch>[0]): boolean {
  const url = input instanceof Request ? input.url : String(input);
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return false;
  }

  const decodedPath = decodeURIComponent(parsed.pathname).toLowerCase();
  return decodedPath.endsWith("/rest/services/city/parcels/featureserver/0/query");
}
