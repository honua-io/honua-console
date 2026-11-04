import { existsSync } from "node:fs";

import { VENDORED_STUDIO_ROOT } from "../../lib/studio-release.mjs";

export const skipWithoutVendoredStudio = existsSync(VENDORED_STUDIO_ROOT) || process.env.CI
  ? false
  : "the Studio release is not vendored; run node scripts/fetch-studio-release.mjs";
