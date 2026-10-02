import fs from 'node:fs';
import path from 'node:path';
import { test as setup, expect } from '@playwright/test';
import {
  establishGovernedOperatorSession,
  operatorStoragePath,
  writeOperatorCallbackEvidence,
} from './operator-session';

// Runs once before the live specs. The storage state is the Console operator cookie
// plus a marker; the server bearer stays in the Console process, bound to that
// operator. An already-authenticated /auth/login preserves a forwardable bearer.

setup.setTimeout(120_000);

setup('sign in through the governed server session before privileged browser calls', async ({ page }) => {
  const evidence = await establishGovernedOperatorSession(page);
  expect(evidence, 'the shared session is established once, in this setup').not.toBeNull();
  writeOperatorCallbackEvidence(evidence!);
  fs.mkdirSync(path.dirname(operatorStoragePath), { recursive: true });
  await page.context().storageState({ path: operatorStoragePath });
});
