// Hand-authored 3D Tiles 1.0 point cloud. Coordinates are metres relative to
// WGS84's equatorial surface at longitude/latitude 0 (ECEF x = 6378137).
// Three non-collinear points ensure a loaded scene cannot pass with an empty tile.
export function scenePoints() {
  const positions = [0, -10, 0, 0, 10, 0, 0, 0, 20];
  const featureJson = JSON.stringify({
    POINTS_LENGTH: 3,
    RTC_CENTER: [6378137, 0, 0],
    POSITION: { byteOffset: 0 },
    RGB: { byteOffset: 36 },
  });
  const json = Buffer.from(featureJson.padEnd(Math.ceil((28 + featureJson.length) / 8) * 8 - 28, ' '));
  const binary = Buffer.alloc(48);
  positions.forEach((value, index) => binary.writeFloatLE(value, index * 4));
  Buffer.from([255, 0, 0, 0, 255, 0, 0, 0, 255]).copy(binary, 36);
  const header = Buffer.alloc(28);
  header.write('pnts');
  header.writeUInt32LE(1, 4);
  header.writeUInt32LE(header.length + json.length + binary.length, 8);
  header.writeUInt32LE(json.length, 12);
  header.writeUInt32LE(binary.length, 16);
  return Buffer.concat([header, json, binary]);
}
