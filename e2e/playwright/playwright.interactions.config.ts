import { defineConfig } from '@playwright/test';
import workspace from './playwright.workspace.config';

// Real installed Console + Studio + server + model, with an already signed-in
// human. Reuse the workspace lane's authentication and origin checks. Never
// launch a source build or fall back to a fixture agent in this lane.
export default defineConfig(workspace, {
  testDir: './interactions',
  testMatch: '**/*.spec.ts',
  retries: 0,
  repeatEach: 1,
  workers: 1,
  fullyParallel: false,
  outputDir: './test-results/interactions/artifacts',
  reporter: [
    ['list'],
    ['json', { outputFile: './test-results/interactions/report.json' }],
  ],
  use: {
    // Keep screenshots, prompts, authorization headers and network traces out
    // of the evidence attachments. Store the runner report privately: errors
    // from a failed assertion can still contain evaluation deployment data.
    trace: 'off',
    video: 'off',
    screenshot: 'off',
  },
});
