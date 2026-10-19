import test from 'node:test';
import assert from 'node:assert/strict';
import {BrowserImportSelection} from '../../src/Honua.Console.Web/wwwroot/js/dataset-import-selection.mjs';
const hash='a'.repeat(64);
const workspace=()=>({session:{environmentId:'env',principalId:'human',releaseId:'release'},workspaceId:'workspace',viewId:'view',
    context:{targetId:'target',revision:2}});
const file=()=>new File(['data'],'places.geojson');
test('only complete-file metadata enters original turn; exact task gets original memory File',async()=>{
    const selection=new BrowserImportSelection({uuid:()=> 'selected'}),state=workspace(),original=file();
    const metadata=await selection.prepare(original,state,{hashFile:async(value)=>{assert.equal(value,original);return hash;}});
    const captured=selection.forTurn(state);assert.deepEqual(captured.metadata,{selectionId:'selected',fileName:'places.geojson',totalBytes:4,expectedSha256:hash});
    assert.equal(JSON.stringify(captured).includes('csrf'),false);
    selection.captured(state,{turnId:'turn',context:{workspaceId:'workspace',targetId:'target',viewId:'view',revision:2},browserFileSelection:metadata},captured);
    assert.equal(selection.forTurn(state),null);
    const controller={task:{source:{kind:'browser-file',browserSelectionId:'selected',fileName:'places.geojson',totalBytes:4,expectedSha256:hash}},
        record:{authority:{originatingTurnId:'turn'}}};
    state.context.revision=3;assert.equal(selection.matchingFile(state,controller),original);
    controller.task.source.browserSelectionId='other';assert.equal(selection.matchingFile(state,controller),null);
});
test('navigation or authority change during hash discards selection',async()=>{
    const selection=new BrowserImportSelection(),state=workspace();let release;
    const pending=selection.prepare(file(),state,{hashFile:()=>new Promise(resolve=>release=resolve)});
    state.context.targetId='other';release(hash);await assert.rejects(pending,/workspace changed/);assert.equal(selection.record,null);
});
test('newer file selection cannot be overwritten by older hash completion',async()=>{
    const selection=new BrowserImportSelection(),state=workspace();let release;
    const older=selection.prepare(file(),state,{hashFile:()=>new Promise(resolve=>release=resolve)});
    await selection.prepare(new File(['new-data'],'new.geojson'),state,{hashFile:async()=>hash});
    release(hash);await assert.rejects(older,/workspace changed/);assert.equal(selection.record.metadata.fileName,'new.geojson');
});
test('stale context cannot attach file metadata to a later message',async()=>{
    const selection=new BrowserImportSelection(),state=workspace();await selection.prepare(file(),state,{hashFile:async()=>hash});
    state.context.revision++;assert.throws(()=>selection.forTurn(state),/earlier context/);
});
test('changed server turn selection is rejected and never binds File to task',async()=>{
    const selection=new BrowserImportSelection(),state=workspace();await selection.prepare(file(),state,{hashFile:async()=>hash});
    const captured=selection.forTurn(state);
    assert.throws(()=>selection.captured(state,{turnId:'turn',context:{workspaceId:'workspace',targetId:'target',viewId:'view',revision:2},
        browserFileSelection:{...captured.metadata,totalBytes:5}},captured),/did not confirm/);assert.equal(selection.record.turnId,null);
});
test('reload has no retained File and requires human reselection',()=>{
    assert.equal(new BrowserImportSelection().record,null);
});
