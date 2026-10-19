import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile,readdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {resolve,join} from 'node:path';
const root=resolve('src/Honua.Console.Web/wwwroot/_studio');
test('production Studio assets are complete and pinned to reviewed source bytes',async()=>{
    const manifest=JSON.parse(await readFile(join(root,'honua-studio-artifact.json'),'utf8'));
    assert.equal(manifest.schemaVersion,'honua.console.studio-artifact.v1');
    assert.match(manifest.studioRevision,/^[a-f0-9]{40}$/);
    assert.match(manifest.sdkRevision,/^[a-f0-9]{40}$/);
    const paths=new Set();
    for(const entry of manifest.files){
        assert.ok(!entry.path.startsWith('/')&&!entry.path.split('/').includes('..'));
        assert.ok(!paths.has(entry.path));paths.add(entry.path);
        const bytes=await readFile(join(root,entry.path));
        assert.equal(bytes.length,entry.bytes,entry.path);
        assert.equal(createHash('sha256').update(bytes).digest('hex'),entry.sha256,entry.path);
    }
    for(const path of ['console.js','style.css','LICENSE','THIRD-PARTY-NOTICES.txt','STUDIO-PROVENANCE.json','SDK-PROVENANCE.json'])assert.ok(paths.has(path),path);
    async function walk(directory,prefix=''){
        const found=[];
        for(const entry of await readdir(directory,{withFileTypes:true})){
            assert.ok(!entry.isSymbolicLink());const path=prefix+entry.name;
            if(entry.isDirectory())found.push(...await walk(join(directory,entry.name),path+'/'));
            else if(path!=='honua-studio-artifact.json')found.push(path);
        }
        return found;
    }
    assert.deepEqual((await walk(root)).sort(),[...paths].sort());
    const main=await readFile(join(root,'console.js'),'utf8');
    assert.ok(main.includes('createWorkspaceAgentSession'));
    assert.ok(!main.includes('__honuaStudioFixtureToken'));
});

test('embedded Studio initializes Maputnik from the pinned Console asset root',async()=>{
 const source=await readFile(new URL('../../src/Honua.Console.Web/wwwroot/js/studio-embed.mjs',import.meta.url),'utf8');
 assert.match(source,/app = document\.createElement\('honua-studio-app'\);\s*app\.styleEditorAssetsUrl = '\/_studio\/maputnik\/index\.html';/);
});
