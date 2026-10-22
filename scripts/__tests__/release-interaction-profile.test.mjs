import {test} from 'node:test';
import assert from 'node:assert/strict';
import {releasePrompt} from '../../e2e/playwright/interactions/release-profile.mjs';
test('Console consumes exact Release A/B prompt and fails on held-out or absent cases',()=>{
    const profile={scenarios:[{id:'IX-01',subcases:[{id:'selected-map',prompts:{A:'This map.',B:'The open map.',C:'held out'}}]}]};
    assert.equal(releasePrompt(profile,'IX-01','selected-map','B'),'The open map.');
    assert.throws(()=>releasePrompt(profile,'IX-01','selected-map','C'),/A\/B/);
    assert.throws(()=>releasePrompt(profile,'IX-01','missing'),/Missing Release prompt/);
});
