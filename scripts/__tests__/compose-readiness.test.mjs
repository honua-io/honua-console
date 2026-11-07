import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { test } from 'node:test';

const composeFiles = ['../../e2e/docker-compose.yml', '../../e2e/playwright/live-llm/docker-compose.yml'];

for (const composeFile of composeFiles) {
  test(`${composeFile} readiness succeeds only on HTTP 200 using released-image tools`, async () => {
    const source = readFileSync(new URL(composeFile, import.meta.url), 'utf8');
    const probes = [...source.matchAll(/test:\s*(\["CMD-SHELL"[^\n]+\])/g)];
    const readiness = probes.map(match => JSON.parse(match[1])[1]).find(probe => probe.includes('/healthz/ready'));
    assert.ok(readiness, 'server readiness probe must exist');
    assert.match(readiness, /bash -c/);
    assert.doesNotMatch(readiness, /\b(?:wget|curl)\b/);
    let status = 200;
    const server = createServer((request, response) => {
      assert.equal(request.url, '/healthz/ready');
      response.writeHead(status);
      response.end();
    });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    try {
      const command = readiness.replaceAll('$$', '$').replace('/127.0.0.1/8080', `/127.0.0.1/${server.address().port}`);
      for (const [code, expectedExit] of [[200, 0], [503, 1]]) {
        status = code;
        const child = spawn('/bin/sh', ['-c', command], { stdio: 'ignore' });
        const [exitCode] = await once(child, 'exit');
        assert.equal(exitCode, expectedExit, `HTTP ${code}`);
      }
    } finally {
      await new Promise(resolve => server.close(resolve));
    }
  });
}
