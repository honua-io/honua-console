const image = process.env.HONUA_CONSOLE_E2E_SERVER_IMAGE ?? '';
const pinned = /^ghcr\.io\/honua-io\/honua-server(?::[A-Za-z0-9_.-]+)?@sha256:[0-9a-f]{64}$/;
if (!pinned.test(image)) {
  console.error('Set server_image or a Console live-server variable to an immutable honua-server GHCR digest.');
  process.exit(1);
}
console.log(`Using immutable server image ${image}`);
