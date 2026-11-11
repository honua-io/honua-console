import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {join} from 'node:path';
import {VENDORED_STUDIO_ROOT as root,readStudioReleaseLock,verifyStudioAssets} from '../lib/studio-release.mjs';
import {skipWithoutVendoredStudio} from './helpers/vendored-studio.mjs';
test('bundled recipe key validates in current browser Unicode Sets mode',{skip:skipWithoutVendoredStudio},async()=>{
    const bundle=await readFile(join(root,'console.js'),'utf8');
    const tag=bundle.match(/<input\b[^>]*data-testid="recipe-package-key"[^>]*>/)?.[0];
    assert.ok(tag,'embedded recipe key input is present');
    const source=tag.match(/\bpattern="([^"]+)"/)?.[1];
    assert.ok(source,'embedded recipe input declares its key constraint');
    const pattern=new RegExp(`^(?:${source})$`,'v');
    for(const key of ['map','map-1','map_a.b']) assert.equal(pattern.test(key),true,key);
    for(const key of ['', 'two words','map/1']) assert.equal(pattern.test(key),false,key);
});
test('the vendored Studio tree is the production bundle of the locked release',{skip:skipWithoutVendoredStudio},async()=>{
    const lock=await readStudioReleaseLock();
    await verifyStudioAssets(root,lock);
    const provenance=JSON.parse(await readFile(join(root,'STUDIO-PROVENANCE.json'),'utf8'));
    assert.equal(provenance.releaseQualified,false);
    assert.equal(provenance.release.tag,lock.tag);
    const main=await readFile(join(root,'console.js'),'utf8');
    assert.ok(main.includes('createWorkspaceAgentSession'));
    assert.ok(!main.includes('__honuaStudioFixtureToken'));
});

test('embedded Studio initializes Maputnik from the pinned Console asset root',async()=>{
 const source=await readFile(new URL('../../src/Honua.Console.Web/wwwroot/js/studio-embed.mjs',import.meta.url),'utf8');
 assert.ok(source.includes("app.styleEditorAssetsUrl = new URL('../_studio/maputnik/index.html', import.meta.url).href;"));
});

test('Console forwards Studio catalog and direct agent MCP to the server MCP endpoint',async()=>{
 const catalog=await readFile(new URL('../../tests/Honua.Console.LegacyHost/StudioReadBff.cs',import.meta.url),'utf8');
 const agent=await readFile(new URL('../../tests/Honua.Console.LegacyHost/AgentSessionBff.cs',import.meta.url),'utf8');
 assert.match(catalog,/SendAsync\(context, "mcp", new ByteArrayContent\(buffer\.ToArray\(\)\), token, catalog: true\)/);
 assert.match(agent,/"mcp" => "mcp"/);
});

test('the New map package-key pattern validates in current browser Unicode Sets mode', async () => {
    const source = await readFile('src/Honua.Console.Web/wwwroot/js/studio-draft-create.mjs', 'utf8');
    const pattern = source.match(/key\.pattern = '([^']+)'/)?.[1];
    assert.ok(pattern, 'the actual New map form must declare package-key validation');
    const expression = new RegExp(`^(?:${pattern})$`, 'v');
    for (const key of ['customer-map', 'map_v1.2', 'A', 'a'.repeat(200)]) assert.ok(expression.test(key), key);
    for (const key of ['', 'has space', 'path/map', '<map>', 'é', 'a'.repeat(201)]) assert.equal(expression.test(key), false, key);
});
