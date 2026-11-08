import assert from 'node:assert/strict';
import test from 'node:test';
import { decide } from '../../src/Honua.Console.Web/wwwroot/interop/human-proposals.js';

const command = {
    action: 'approve', profileId: 'environment', authority: 1, proposalId: 'proposal/id',
    decision: { proposalId: 'proposal/id', proposalVersion: 7, planHash: 'plan', environmentReference: 'binding' },
};

function response(body, status = 200) {
    return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

for (const action of ['review', 'approve']) {
    test(`decide ${action} uses the current cookie session and one CSRF-protected request`, async context => {
        const calls = [];
        context.mock.method(globalThis, 'fetch', async (url, options) => {
            calls.push({ url, options });
            return calls.length === 1
                ? response({ profileId: command.profileId, csrfToken: 'csrf' })
                : response({ status: 'Allowed' });
        });
        const request = { ...command, action, decision: action === 'review' ? null : command.decision };

        assert.deepEqual(await decide(request), { status: 'Allowed' });

        assert.equal(calls.length, 2);
        assert.equal(calls[0].url, '/api/console/proposals/session');
        for (const { options } of calls) {
            assert.equal(options.credentials, 'same-origin');
            assert.equal(options.cache, 'no-store');
            assert.equal(options.redirect, 'error');
        }
        assert.equal(calls[1].url, `/api/console/proposals/proposal%2Fid/${action}`);
        assert.equal(calls[1].options.method, 'POST');
        assert.deepEqual(calls[1].options.headers, { 'Content-Type': 'application/json', 'X-CSRF-Token': 'csrf' });
        assert.deepEqual(JSON.parse(calls[1].options.body), {
            profileId: request.profileId, authority: request.authority, decision: request.decision,
        });
    });
}

for (const bootstrap of [{ profileId: 'another-environment', csrfToken: 'csrf' }, { profileId: 'environment' }]) {
    test(`decide refuses an unbound browser session: ${JSON.stringify(bootstrap)}`, async context => {
        const fetch = context.mock.method(globalThis, 'fetch', async () => response(bootstrap));

        await assert.rejects(decide(command), /environment changed/);

        assert.equal(fetch.mock.callCount(), 1);
    });
}

test('decide refuses an unauthenticated bootstrap before submission', async context => {
    const fetch = context.mock.method(globalThis, 'fetch', async () => response({}, 401));

    await assert.rejects(decide(command), /session is unavailable/);

    assert.equal(fetch.mock.callCount(), 1);
});

test('decide does not retry an uncertain decision', async context => {
    let calls = 0;
    context.mock.method(globalThis, 'fetch', async () => ++calls === 1
        ? response({ profileId: 'environment', csrfToken: 'csrf' })
        : response({}, 503));

    await assert.rejects(decide(command), /decision status/);

    assert.equal(calls, 2);
});

test('decide refuses an unknown action before network access', async context => {
    const fetch = context.mock.method(globalThis, 'fetch', async () => { throw new Error('Unexpected request'); });

    await assert.rejects(decide({ ...command, action: 'execute' }), /Unknown human decision/);

    assert.equal(fetch.mock.callCount(), 0);
});
