import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export function verifyImportStatusContract(serverSource, contract) {
  const source = serverSource.replace(/\/\*[\s\S]*?\*\/|\/\/[^\n]*/g, '');
  const body = source.match(/public\s+enum\s+GeoservicesImportStatus\s*\{([^}]+)\}/)?.[1];
  assert.ok(body, 'Server import status enum is missing');
  let nextCode = 0;
  const statuses = body.split(',').map(member => member.trim()).filter(Boolean).map(member => {
    const match = member.match(/^([A-Za-z_]\w*)(?:\s*=\s*(\d+))?$/);
    assert.ok(match, `Unsupported server enum member: ${member}`);
    const code = match[2] === undefined ? nextCode : Number(match[2]);
    nextCode = code + 1;
    return { code, name: match[1] };
  });
  const byCode = (left, right) => left.code - right.code;
  assert.deepEqual(statuses.sort(byCode), contract.map(({ code, name }) => ({ code, name })).sort(byCode),
    'Server import status wire values differ from the Console/SDK contract');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [serverPath, contractPath] = process.argv.slice(2);
  assert.ok(serverPath && contractPath, 'Usage: check-import-status-contract.mjs <server-source> <contract-json>');
  const contract = JSON.parse(readFileSync(contractPath, 'utf8'));
  verifyImportStatusContract(readFileSync(serverPath, 'utf8'), contract);
  console.log(`Server import status contract matches all ${contract.length} Console/SDK states.`);
}
