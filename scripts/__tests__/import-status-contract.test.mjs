import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { verifyImportStatusContract } from '../check-import-status-contract.mjs';

const contract = [{ code: 0, name: 'Queued' }, { code: 1, name: 'Completed' }];

test('accepts the actual implicit and explicit wire values', () => {
  verifyImportStatusContract('public enum GeoservicesImportStatus { Queued, Completed }', contract);
  verifyImportStatusContract('public enum GeoservicesImportStatus { Queued = 0, /* terminal */ Completed = 1, }', contract);
  verifyImportStatusContract('public enum GeoservicesImportStatus { Completed = 1, Queued = 0 }', contract);
  verifyImportStatusContract('public enum GeoservicesImportStatus { Queued, // queued job\n Completed }', contract);
});

for (const members of ['Queued, Validating, Completed', 'Queued', 'Queued, Cancelled',
  'Queued = 1, Completed', 'Queued, Completed = 8', 'Queued, Completed = Unknown']) {
  test(`rejects server contract drift: ${members}`, () => {
    assert.throws(() => verifyImportStatusContract(`public enum GeoservicesImportStatus { ${members} }`, contract));
  });
}

test('rejects missing or renamed server enum', () => {
  assert.throws(() => verifyImportStatusContract('public enum OtherStatus { Queued, Completed }', contract));
});

test('CLI accepts the real file inputs and fails on wire drift', () => {
  const directory = mkdtempSync(join(tmpdir(), 'honua-import-contract-'));
  try {
    const serverPath = join(directory, 'server.cs');
    const contractPath = join(directory, 'contract.json');
    const script = fileURLToPath(new URL('../check-import-status-contract.mjs', import.meta.url));
    writeFileSync(contractPath, JSON.stringify(contract));
    writeFileSync(serverPath, 'public enum GeoservicesImportStatus { Queued, Completed }');
    const passed = spawnSync(process.execPath, [script, serverPath, contractPath], { encoding: 'utf8' });
    assert.equal(passed.status, 0, passed.stderr);
    writeFileSync(serverPath, 'public enum GeoservicesImportStatus { Queued, Completed = 8 }');
    const failed = spawnSync(process.execPath, [script, serverPath, contractPath], { encoding: 'utf8' });
    assert.equal(failed.status, 1);
    assert.match(failed.stderr, /wire values differ/);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
