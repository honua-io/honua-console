import {test} from 'node:test';
import assert from 'node:assert/strict';
import {assertHumanProposer} from '../../e2e/playwright/interactions/publication-witness.mjs';

test('canonical human proposer permits nullable or omitted agent identifier',()=>{
    assert.doesNotThrow(()=>assertHumanProposer({requestedBy:'human:one',requestedByAgent:null}));
    assert.doesNotThrow(()=>assertHumanProposer({requestedBy:'human:one'}));
});
test('agent identifiers and unexpected wire types cannot count as human provenance',()=>{
    for (const requestedByAgent of ['agent:one','',false,true,{},0])
        assert.throws(()=>assertHumanProposer({requestedBy:'human:one',requestedByAgent}));
    for (const requestedBy of ['',null,{},undefined]) assert.throws(()=>assertHumanProposer({requestedBy}));
});

test('final same-operation request resource is mandatory, pending identity optional and exact',async()=>{
    const {publicationRequestId}=await import('../../e2e/playwright/interactions/publication-witness.mjs');
    const operation={resourceIds:{requestId:'request-one'}};
    assert.equal(publicationRequestId(operation),'request-one');
    assert.equal(publicationRequestId(operation,'request-one'),'request-one');
    for(const invalid of [null,'',false,{},'two words','another-request']) assert.throws(()=>publicationRequestId(operation,invalid));
    for(const requestId of [undefined,null,'',false,{},'two words']) assert.throws(()=>publicationRequestId({resourceIds:{requestId}}));
});
