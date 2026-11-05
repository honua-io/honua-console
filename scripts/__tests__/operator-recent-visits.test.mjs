import test from 'node:test';
import assert from 'node:assert/strict';
import {OperatorRecentVisits} from '../../src/Honua.Console.Web/wwwroot/js/operator-recent-visits.mjs';

function storage() {
    const records = new Map();
    return {records, getItem: key => records.get(key), setItem: (key, value) => records.set(key, value),
        removeItem: key => records.delete(key)};
}
const partition = overrides => ({key: 'preferences:operator-one:environment-one', authority: 'release-one',
    expiresAt: Date.now() + 60000, ...overrides});

test('recent visits reload only in the validated operator and environment partition', () => {
    const browser = storage(), history = new OperatorRecentVisits(browser);
    history.bind(partition()); history.record({path: '/operate/services/roads/settings', label: 'Roads'});
    const reloaded = new OperatorRecentVisits(browser);
    reloaded.bind(partition()); assert.deepEqual(reloaded.items, history.items);
    for (const key of ['preferences:operator-two:environment-one', 'preferences:operator-one:environment-two']) {
        reloaded.bind(partition({key})); assert.deepEqual(reloaded.items, []);
    }
    reloaded.bind(partition()); assert.equal(reloaded.items[0].label, 'Roads');
});

test('recent visits deduplicate and retain only six current links', () => {
    const history = new OperatorRecentVisits(storage()); history.bind(partition());
    for (let index = 0; index < 9; index++) history.record({path: `/operate/layers/layer-${index}`, label: `Layer ${index}`});
    history.record({path: '/operate/layers/layer-7', label: 'Updated label'});
    assert.equal(history.items.length, 6);
    assert.deepEqual(history.items.map(item => item.path), [7, 8, 6, 5, 4, 3].map(index => `/operate/layers/layer-${index}`));
    assert.equal(history.items[0].label, 'Updated label');
});

for (const path of ['//example.test', 'https://example.test', '/auth/login', '/embed/maps/one',
    '/operate/../auth', '/operate/%2e%2e/auth', '/operate/data?secret=value', '/operate/data#token', '/missing-route']) {
    test(`recent visits reject unsupported or credential-bearing locations: ${path}`, () => {
        const browser = storage(), history = new OperatorRecentVisits(browser);
        history.bind(partition()); history.record({path, label: 'Untrusted location'});
        assert.deepEqual(history.items, []); assert.equal(browser.records.size, 0);
    });
}

test('changed source authority invalidates saved visits instead of reviving them later', () => {
    const history = new OperatorRecentVisits(storage()); history.bind(partition());
    history.record({path: '/operate/data', label: 'Data & Layers'});
    history.bind(partition({authority: 'release-two'})); assert.deepEqual(history.items, []);
    history.bind(partition()); assert.deepEqual(history.items, []);
});

test('expired leases cannot write visits and malformed storage does not break navigation', () => {
    const browser = storage(), history = new OperatorRecentVisits(browser);
    history.bind(partition()); history.expiresAt = Date.now() - 1;
    history.record({path: '/operate/data', label: 'Data'});
    assert.equal(browser.records.size, 0); assert.deepEqual(history.items, []);
    const invalid = new OperatorRecentVisits({getItem: () => '{broken'});
    invalid.bind(partition()); assert.deepEqual(invalid.items, []);
});

test('blocked browser storage retains same-partition visits in memory', () => {
    const blocked = {getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); }};
    const history = new OperatorRecentVisits(blocked); history.bind(partition());
    history.record({path: '/operate/health', label: 'Health'}); history.bind(partition());
    assert.equal(history.items[0].label, 'Health');
    history.bind(partition({key: 'preferences:operator-two:environment-one'})); assert.deepEqual(history.items, []);
});
