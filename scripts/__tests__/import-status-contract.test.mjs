import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyImportStatusContract } from '../check-import-status-contract.mjs';

const contract = [{ code: 0, name: 'Queued' }, { code: 1, name: 'Completed' }];

test('accepts the actual implicit and explicit wire values', () => {
  verifyImportStatusContract('public enum GeoservicesImportStatus { Queued, Completed }', contract);
  verifyImportStatusContract('public enum GeoservicesImportStatus { Queued = 0, /* terminal */ Completed = 1, }', contract);
  verifyImportStatusContract('public enum GeoservicesImportStatus { Completed = 1, Queued = 0 }', contract);
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
