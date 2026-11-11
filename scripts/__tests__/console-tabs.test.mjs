import assert from 'node:assert/strict';
import test from 'node:test';
import { attach, detach } from '../../src/Honua.Console.Shell/wwwroot/console-tabs.js';

function tabList() {
    let listener;
    const visits = [];
    const tabs = ['layers', 'protocols', 'access'].map(id => ({
        focus: () => visits.push(`focus:${id}`),
        click: () => visits.push(`select:${id}`)
    }));
    const element = {
        querySelectorAll: () => tabs,
        addEventListener: (name, callback) => { assert.equal(name, 'keydown'); listener = callback; },
        removeEventListener: (name, callback) => { assert.equal(name, 'keydown'); if (listener === callback) listener = undefined; }
    };
    function press(key, index) {
        let prevented = false;
        listener?.({ key, target: tabs[index], preventDefault: () => { prevented = true; } });
        return prevented;
    }
    return { element, visits, press };
}

test('tabs wrap arrow navigation and support Home and End without trapping Tab', () => {
    const list = tabList();
    attach(list.element);
    assert.equal(list.press('ArrowLeft', 0), true);
    assert.deepEqual(list.visits.splice(0), ['focus:access', 'select:access']);
    assert.equal(list.press('ArrowRight', 2), true);
    assert.deepEqual(list.visits.splice(0), ['focus:layers', 'select:layers']);
    list.press('End', 0);
    assert.deepEqual(list.visits.splice(0), ['focus:access', 'select:access']);
    list.press('Home', 2);
    assert.deepEqual(list.visits.splice(0), ['focus:layers', 'select:layers']);
    assert.equal(list.press('Tab', 0), false);
    assert.deepEqual(list.visits, []);
});

test('tabs detach their listener on disposal', () => {
    const list = tabList();
    attach(list.element);
    detach(list.element);
    assert.equal(list.press('ArrowRight', 0), false);
    assert.deepEqual(list.visits, []);
});
