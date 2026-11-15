import test from 'node:test';
import assert from 'node:assert/strict';
import { consoleHref } from '../../src/Honua.Console.Web/wwwroot/js/console-links.mjs';

test('app paths stay inside the static bundle base', () => {
    const base = 'https://ops.example/console/';
    assert.equal(consoleHref('/inbox?proposalId=p%201', base), '/console/inbox?proposalId=p%201');
    assert.equal(consoleHref('/operate/jobs/job-1', base), '/console/operate/jobs/job-1');
    assert.equal(consoleHref('/maps/published/a/b#top', base), '/console/maps/published/a/b#top');
});

test('the legacy root host and a missing document keep the original path', () => {
    assert.equal(consoleHref('/inbox?proposalId=p', 'http://127.0.0.1:5274/'), '/inbox?proposalId=p');
    assert.equal(consoleHref('/studio', undefined), '/studio');
});

test('anything other than an app path is refused', () => {
    for (const value of ['inbox', '//evil.example/x', 'https://evil.example/', '/\\evil', '', null])
        assert.throws(() => consoleHref(value, 'https://ops.example/console/'), /app path/);
});
