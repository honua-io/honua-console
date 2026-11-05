import test from 'node:test';
import assert from 'node:assert/strict';
import {OperatorPreferences} from '../../src/Honua.Console.Web/wwwroot/js/operator-preferences.mjs';

function storage() {
    const records = new Map();
    return {getItem: key => records.get(key) ?? null, setItem: (key, value) => records.set(key, value),
        removeItem: key => records.delete(key), records};
}
const session = overrides => ({authenticated: true, principalId: 'opaque-operator', environmentId: 'environment-one',
    expiresAt: new Date(Date.now() + 60000).toISOString(), releaseId: 'release-one', sourcePathBase: '', ...overrides});

test('follow preference survives a reload only in its operator and environment partition', () => {
    const browser = storage(), preferences = new OperatorPreferences(browser);
    preferences.bind(session()); preferences.save({follow: false});
    const reloaded = new OperatorPreferences(browser);
    reloaded.bind(session()); assert.equal(reloaded.follow, false);
    reloaded.bind(session({principalId: 'operator-two'})); assert.equal(reloaded.follow, true);
    reloaded.bind(session({environmentId: 'environment-two'})); assert.equal(reloaded.follow, true);
    reloaded.bind(session()); assert.equal(reloaded.follow, false);
    assert.deepEqual([...browser.records.keys()], ['honua.shell.preferences.v1:opaque-operator:environment-one']);
});

for (const changed of [{releaseId: 'release-two'}, {sourcePathBase: '/another'}]) {
    test(`authority change invalidates stored follow preference: ${Object.keys(changed)[0]}`, () => {
        const browser = storage(), preferences = new OperatorPreferences(browser);
        preferences.bind(session()); preferences.save({follow: false});
        preferences.bind(session(changed));
        assert.equal(preferences.follow, true); assert.equal(browser.records.size, 0);
    });
}

for (const invalid of [{authenticated: false}, {principalId: 'raw@example.test'}, {environmentId: ''}, {expiresAt: 'invalid'}, {expiresAt: '2000-01-01'}]) {
    test(`unvalidated identity has ephemeral preferences: ${Object.keys(invalid)[0]} ${invalid.expiresAt ?? ''}`, () => {
        const browser = storage(), preferences = new OperatorPreferences(browser);
        preferences.bind(session(invalid)); preferences.save({follow: false});
        assert.equal(browser.records.size, 0); assert.equal(preferences.key, null);
        preferences.clear(); assert.equal(preferences.follow, true);
    });
}

test('blocked or malformed browser storage keeps the in-memory choice and usable ephemeral controls', () => {
    const broken = {getItem() {throw new Error('blocked');}, setItem() {throw new Error('blocked');}};
    const preferences = new OperatorPreferences(broken);
    preferences.bind(session()); preferences.save({follow: false}); assert.equal(preferences.follow, false);
    preferences.bind(session()); assert.equal(preferences.follow, false);
    const malformed = new OperatorPreferences({getItem: () => '{broken'});
    malformed.bind(session()); assert.equal(malformed.follow, true);
});

test('an expired validated partition cannot persist a later preference edit', () => {
    const browser = storage(), preferences = new OperatorPreferences(browser);
    preferences.bind(session()); preferences.expiresAt = Date.now() - 1;
    preferences.save({follow: false});
    assert.equal(preferences.key, null); assert.equal(browser.records.size, 0);
    assert.equal(preferences.follow, false);
});
