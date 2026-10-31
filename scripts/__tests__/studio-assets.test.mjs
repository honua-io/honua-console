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
    const provenance=JSON.parse(await readFile(join(root,'STUDIO-PROVENANCE.json'),'utf8'));
    const sdk=JSON.parse(await readFile(join(root,'SDK-PROVENANCE.json'),'utf8'));
    assert.equal(provenance.schema,'honua.studio.console-assets/v1');
    assert.equal(provenance.sourceCommit,manifest.studioRevision);
    assert.equal(provenance.sdk.sourceCommit,manifest.sdkRevision);
    assert.deepEqual(provenance.sdk,sdk);
    assert.equal(provenance.releaseQualified,false);
    assert.deepEqual(provenance.files,Object.fromEntries(manifest.files
        .filter(entry=>entry.path!=='STUDIO-PROVENANCE.json')
        .map(({path,bytes,sha256})=>[path,{bytes,sha256}])));
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

test('Console forwards Studio catalog and direct agent MCP to the server MCP endpoint',async()=>{
 const catalog=await readFile(new URL('../../src/Honua.Console.Web/StudioReadBff.cs',import.meta.url),'utf8');
 const agent=await readFile(new URL('../../src/Honua.Console.Web/AgentSessionBff.cs',import.meta.url),'utf8');
 assert.match(catalog,/SendAsync\(context, "mcp", new ByteArrayContent\(buffer\.ToArray\(\)\), token, catalog: true\)/);
 assert.match(agent,/"mcp" => "mcp"/);
});

test('the New map package-key pattern validates in current browser Unicode Sets mode', async () => {
    const source = await readFile('src/Honua.Console.Web/wwwroot/js/studio-embed.mjs', 'utf8');
    const pattern = source.match(/key\.pattern='([^']+)'/)?.[1];
    assert.ok(pattern, 'the actual New map form must declare package-key validation');
    const expression = new RegExp(`^(?:${pattern})$`, 'v');
    for (const key of ['customer-map', 'map_v1.2', 'A', 'a'.repeat(200)]) assert.ok(expression.test(key), key);
    for (const key of ['', 'has space', 'path/map', '<map>', 'é', 'a'.repeat(201)]) assert.equal(expression.test(key), false, key);
});
