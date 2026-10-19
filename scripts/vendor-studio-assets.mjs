#!/usr/bin/env node
import { cp, mkdir, readdir, readFile, writeFile, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, join, relative } from 'node:path';
const [sourceArg, studioRevision, sdkRevision] = process.argv.slice(2);
if (!sourceArg || !/^[a-f0-9]{40}$/.test(studioRevision || '') || !/^[a-f0-9]{40}$/.test(sdkRevision || ''))
    throw new Error('Usage: vendor-studio-assets.mjs BUILD_DIRECTORY STUDIO_COMMIT_SHA SDK_COMMIT_SHA');
const source = resolve(sourceArg);
const destination = resolve('src/Honua.Console.Web/wwwroot/_studio');
const main = await readFile(join(source, 'console.js'), 'utf8');
if (!main.includes('createWorkspaceAgentSession') || main.includes('__honuaStudioFixtureToken'))
    throw new Error('The source is not a production Console Studio entry.');
await readFile(join(source, 'style.css'));
const sdk = JSON.parse(await readFile(join(source, 'SDK-PROVENANCE.json'), 'utf8'));
if (sdk.sourceCommit !== sdkRevision)
    throw new Error('The SDK provenance does not match the requested source revision.');
if (source === destination) throw new Error('Source must be separate from the managed vendor directory.');
await rm(destination, { recursive: true, force: true });
await mkdir(destination, { recursive: true });
await cp(source, destination, { recursive: true });
// Provenance describes this vendoring operation, never a previous bundle's review.
await rm(join(destination, 'STUDIO-PROVENANCE.json'), { force: true });
async function files(directory) {
    const names = [];
    for (const item of await readdir(directory, { withFileTypes: true })) {
        const path = join(directory, item.name);
        if (item.isSymbolicLink()) throw new Error('Studio artifacts must not contain symbolic links.');
        if (item.isDirectory()) names.push(...await files(path));
        else if (item.name !== 'honua-studio-artifact.json') names.push(path);
    }
    return names;
}
const entries = [];
for (const path of (await files(destination)).sort()) {
    const content = await readFile(path);
    entries.push({ path: relative(destination, path).replaceAll('\\', '/'), bytes: content.length,
        sha256: createHash('sha256').update(content).digest('hex') });
}
const provenance = Buffer.from(JSON.stringify({
    schema: 'honua.studio.console-assets/v1', sourceCommit: studioRevision,
    releaseQualified: false, integrationQualification: 'pending-installed-workspace-journey',
    sdk, files: Object.fromEntries(entries.map(({ path, bytes, sha256 }) => [path, { bytes, sha256 }]))
}, null, 2) + '\n');
await writeFile(join(destination, 'STUDIO-PROVENANCE.json'), provenance);
entries.push({ path: 'STUDIO-PROVENANCE.json', bytes: provenance.length,
    sha256: createHash('sha256').update(provenance).digest('hex') });
entries.sort((a, b) => a.path.localeCompare(b.path));
await writeFile(join(destination, 'honua-studio-artifact.json'), JSON.stringify({
    schemaVersion: 'honua.console.studio-artifact.v1', studioRevision, sdkRevision, files: entries
}, null, 2) + '\n');
process.stdout.write(`Vendored ${entries.length} pinned Studio assets (${entries.reduce((sum,entry)=>sum+entry.bytes,0)} bytes).\n`);
