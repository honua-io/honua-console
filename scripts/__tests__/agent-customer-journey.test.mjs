import { test } from 'node:test';
import assert from 'node:assert/strict';
import { exerciseAgentRecovery } from '../agent-customer-journey.mjs';

test('recovery refuses a missing or shared approver before stopping Redis', async () => {
  let stopped = false;
  const stopRedis = async () => { stopped = true; };
  for (const approverKey of ['', 'same']) {
    await assert.rejects(exerciseAgentRecovery({ managerUrl: 'http://127.0.0.1:1', proposerKey: 'same', approverKey,
      stopRedis, observe: async () => {} }), /Separate proposer and approver/);
  }
  assert.equal(stopped, false);
});
