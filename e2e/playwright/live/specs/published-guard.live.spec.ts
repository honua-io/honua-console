import { test, expect } from '@playwright/test';
import { classifyServiceProbe } from '../published';

// Unit coverage for the fixture guard every other live spec skips on.
//
// It lives in the live suite deliberately: honua-release's S4 gate runs THIS suite, and the failure
// mode being guarded against is "the probe quietly reports not-published for a 401/500, so the gate
// goes green over a broken server". Keeping the test next to the specs that depend on it means the
// distinction is re-proved on every run of the lane that would be fooled. It needs no server and no
// browser, so it costs milliseconds.

test.describe('live fixture guard · classifyServiceProbe', () => {
  test('a real FeatureServer payload is published', () => {
    expect(classifyServiceProbe(200, { layers: [{ id: 3, name: 'E2E Source' }] })).toEqual({ state: 'published' });
    // An empty service is still a published service.
    expect(classifyServiceProbe(200, { layers: [] })).toEqual({ state: 'published' });
  });

  test('the Esri 200 + 404 envelope is missing, not an error', () => {
    const envelope = {
      layers: null,
      error: { code: 404, message: 'Not Found', details: ["Service 'e2e_src_fs' not found."] },
    };
    expect(classifyServiceProbe(200, envelope)).toEqual({ state: 'missing' });
  });

  test('a transport 404 is missing too', () => {
    expect(classifyServiceProbe(404, { error: { code: 404 } })).toEqual({ state: 'missing' });
  });

  // The regression this file exists for: these must NOT be laundered into "not published".
  for (const code of [401, 403, 500, 503]) {
    test(`an error envelope with code ${code} is an error, never a skip`, () => {
      const probe = classifyServiceProbe(200, { error: { code, message: `boom ${code}` } });
      expect(probe.state, `code ${code} must not be reported as missing`).toBe('error');
      expect(probe.state === 'error' && probe.reason).toContain(String(code));
    });
  }

  test('a non-2xx status is an error', () => {
    expect(classifyServiceProbe(401, {}).state).toBe('error');
    expect(classifyServiceProbe(500, {}).state).toBe('error');
  });

  test('an unrecognised payload is an error, not a silent skip', () => {
    // No error envelope and no layers[] — the shape changed under us; fail loudly.
    expect(classifyServiceProbe(200, { somethingElse: true }).state).toBe('error');
    expect(classifyServiceProbe(200, null).state).toBe('error');
    expect(classifyServiceProbe(200, 'not json object').state).toBe('error');
    // A non-object error envelope is not something we can reason about.
    expect(classifyServiceProbe(200, { error: 'boom' }).state).toBe('error');
  });
});
