import test from 'node:test';
import assert from 'node:assert/strict';
import * as viewerModule from '../../src/Honua.Console.Web/wwwroot/js/published-map-viewer.mjs';

const {audienceCatalog, createPublishedMapViewer, publishedMapPath} = viewerModule;
const origin = 'https://console.test';
const partition = 'a'.repeat(64);
const publicationUrl = `${origin}/api/studio/viewer/published/map/map-1`;
const dataUrl = `${origin}/api/studio/viewer/data/rest/services/actual-folder/actual-service/FeatureServer/7/query?f=geojson`;

function artifact(version = 'first') {
    const body = {title: `Body ${version}`, layers: [{sourceId: 'opaque-source-id'}], version};
    return {family: 'map', envelope: {family: 'map', title: `Map ${version}`, body}, sourceCatalog: [{
        id: 'opaque-source-id', protocol: 'geoservices-feature-service',
        locator: {url: 'https://server.test/rest/services/actual-folder/actual-service/FeatureServer', layerId: 7},
    }]};
}

function publishedResponse(value = artifact(), status = 200, header = partition) {
    return new Response(JSON.stringify({success: true, data: value}),
        {status, headers: {'Content-Type': 'application/json', 'X-Honua-Viewer-Partition': header}});
}

function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return {promise, resolve};
}

function harness(responder = () => publishedResponse()) {
    const calls = [];
    const renders = [];
    const container = {};
    const widgetContainer = {};
    const status = {textContent: ''};
    const title = {textContent: ''};
    let disposed = 0;
    const fetchImpl = async (url, init) => {
        calls.push({url, init});
        return responder(url, init, calls.length);
    };
    const runtime = {AudienceStyleProvider: class { constructor(options) { Object.assign(this, options); } },
        createReadOnlyCompositionView: options => {
        renders.push(options);
        return {status: 'ready', statusDetail: '', settled: async () => {}, dispose() { disposed++; }};
    }};
    const viewer = createPublishedMapViewer({route: 'map/map-1', container, widgetContainer, status, title,
        origin, fetchImpl, loadRenderer: async () => runtime});
    return {viewer, calls, renders, container, widgetContainer, status, title, runtime, get disposed() { return disposed; }};
}

test('exports only current reader viewer helpers and validates publication routes', () => {
    assert.deepEqual(Object.keys(viewerModule).sort(), ['audienceCatalog', 'createPublishedMapViewer', 'publishedMapPath']);
    assert.equal(publishedMapPath('/map/map-1'), '/maps/published/map/map-1');
    assert.equal(publishedMapPath('/maps/2026.1'), '/maps/published/maps/2026.1');
    assert.equal(publishedMapPath('/maps/Hawaiʻi #1'), '/maps/published/maps/Hawai%CA%BBi%20%231');
    assert.throws(() => publishedMapPath('map/../writer'));
    assert.throws(() => publishedMapPath('map/%2fadmin'));
});

test('current-reader descriptors rebind their actual service path without deriving one from source ID', () => {
    const source = artifact().sourceCatalog[0];
    const catalog = audienceCatalog([source], origin);
    assert.equal(catalog[0].id, 'opaque-source-id');
    assert.equal(catalog[0].locator.url,
        `${origin}/api/studio/viewer/data/rest/services/actual-folder/actual-service/FeatureServer`);
    assert.equal(source.locator.url, 'https://server.test/rest/services/actual-folder/actual-service/FeatureServer');
});

test('loads canonical envelope body untouched into the read-only canvas with current projection', async () => {
    const current = artifact();
    const h = harness(() => publishedResponse(current));
    await h.viewer.reload();
    assert.equal(h.calls[0].url, publicationUrl);
    assert.deepEqual({method: h.calls[0].init.method, credentials: h.calls[0].init.credentials,
        redirect: h.calls[0].init.redirect, cache: h.calls[0].init.cache},
    {method: 'GET', credentials: 'same-origin', redirect: 'error', cache: 'no-store'});
    assert.equal(h.renders.length, 1);
    const options = h.renders[0];
    assert.equal(options.container, h.container);
    assert.equal(options.widgetContainer, h.widgetContainer);
    assert.deepEqual(options.body, current.envelope.body);
    assert.equal(options.projection.baseUrl, `${origin}/api/studio/viewer/data`);
    assert.equal(options.projection.catalog[0].locator.url,
        `${origin}/api/studio/viewer/data/rest/services/actual-folder/actual-service/FeatureServer`);
    assert.equal(options.sourceAuthBaseUrl, `${origin}/api/studio/viewer/data`);
    assert.equal(options.getAudienceTransport().fetchRequest instanceof Function, true);
    assert.equal(options.styleProvider.getTransport(), options.getAudienceTransport());
    assert.equal(h.title.textContent, 'Map first');
    assert.equal(h.status.textContent, 'Map ready.');
    h.viewer.dispose();
});

test('an unavailable renderer never announces a ready map', async () => {
    const h = harness();
    h.runtime.createReadOnlyCompositionView = () => ({status: 'unavailable', settled: async () => {}, dispose() {}});
    await h.viewer.reload();
    assert.equal(h.status.textContent, 'The map is unavailable.');
    h.viewer.dispose();
});

test('canonical dashboard and app compositions open in the same read-only map and widget view', async () => {
    const body = {layers: [], widgets: [{id: 'layer-list', kind: 'toc'}], view: {zoom: 4}};
    for (const family of ['dashboard', 'app']) {
        const value = {...artifact(), family, envelope: {family, body}, sourceCatalog: []};
        const h = harness(() => publishedResponse(value));
        await h.viewer.reload();
        assert.equal(h.renders.length, 1);
        assert.deepEqual(h.renders[0].body, body);
        assert.equal(h.renders[0].widgetContainer, h.widgetContainer);
        assert.equal(h.status.textContent, 'Map ready.');
        h.viewer.dispose();
    }
});

test('data reads use only same-origin cookie and viewer partition, dropping injected credentials', async () => {
    const h = harness((url) => url === publicationUrl ? publishedResponse() :
        new Response('{}', {status: 200, headers: {'X-Honua-Viewer-Partition': partition}}));
    await h.viewer.reload();
    const auth = h.renders[0].getAudienceTransport();
    const response = await auth.fetchRequest(dataUrl, {headers: {
        Authorization: 'Bearer stolen', 'X-API-Key': 'admin', Cookie: 'bad=secret',
    }});
    assert.equal(response.status, 200);
    assert.equal(h.calls.length, 2);
    assert.equal(h.calls[1].url, dataUrl);
    assert.deepEqual(h.calls[1].init.headers,
        {Accept: 'application/json', 'X-Honua-Viewer-Partition': partition});
    assert.equal(h.calls[1].init.credentials, 'same-origin');
    assert.equal(h.calls[1].init.method, 'GET');
    assert.equal(h.calls[1].init.redirect, 'error');
    assert.equal(h.calls[1].init.cache, 'no-store');
    h.viewer.dispose();
});

test('styles and layer metadata use the same current-reader transport with fixed GET headers', async () => {
    const h = harness((url) => url === publicationUrl ? publishedResponse() :
        new Response('{}', {headers: {'X-Honua-Viewer-Partition': partition}}));
    await h.viewer.reload();
    const transport = h.renders[0].getAudienceTransport();
    const style = `${origin}/api/studio/viewer/styles/audience-style`;
    const metadata = `${origin}/api/studio/viewer/data/rest/services/actual-folder/actual-service/FeatureServer/7`;
    await transport.fetchRequest(style, {headers: {Authorization: 'Bearer forbidden'}, credentials: 'include'});
    await transport.fetchRequest(metadata);
    assert.deepEqual(h.calls.slice(1).map(call => [call.url, call.init.headers.Accept]),
        [[style, 'application/vnd.mapbox.style+json'], [metadata, 'application/json']]);
    for (const call of h.calls.slice(1)) {
        assert.equal(call.init.headers['X-Honua-Viewer-Partition'], partition);
        assert.equal(call.init.credentials, 'same-origin');
        assert.equal(call.init.method, 'GET');
    }
    await assert.rejects(transport.fetchRequest(`${style}?draft=true`), /unsupported data route/);
    await assert.rejects(transport.fetchRequest(`${metadata}?f=json`), /unsupported data route/);
    assert.equal(h.calls.length, 3);
    h.viewer.dispose();
});

test('data transport refuses foreign origins and mutation routes before fetch', async () => {
    const h = harness();
    await h.viewer.reload();
    const auth = h.renders[0].getAudienceTransport();
    await assert.rejects(auth.fetchRequest('https://elsewhere.test/rest/services/a/FeatureServer/0/query'));
    await assert.rejects(auth.fetchRequest(dataUrl, {method: 'POST'}));
    await assert.rejects(auth.fetchRequest(
        `${origin}/api/studio/viewer/data/rest/services/actual-folder/actual-service/FeatureServer/7/deleteFeatures`));
    assert.equal(h.calls.length, 1);
    h.viewer.dispose();
});

test('data response from a changed partition is refused', async () => {
    const h = harness(url => url === publicationUrl ? publishedResponse() :
        new Response('{}', {status: 200, headers: {'X-Honua-Viewer-Partition': 'b'.repeat(64)}}));
    await h.viewer.reload();
    await assert.rejects(h.renders[0].getAudienceTransport().fetchRequest(dataUrl), /viewer connection changed/);
    h.viewer.dispose();
});

test('denied, missing, changed and incompatible publications show honest states', async () => {
    for (const [code, expected] of [[401, 'Sign in'], [403, 'cannot view'], [404, 'no longer available'],
        [409, 'changed']]) {
        const h = harness(() => new Response('{}', {status: code}));
        await h.viewer.reload();
        assert.match(h.status.textContent, new RegExp(expected));
        assert.equal(h.renders.length, 0);
    }
    const wrongFamily = harness(() => publishedResponse({...artifact(), family: 'dashboard'}));
    await wrongFamily.viewer.reload();
    assert.match(wrongFamily.status.textContent, /different viewer/);
    assert.equal(wrongFamily.renders.length, 0);

    for (const value of [
        {...artifact(), family: 'query', envelope: {family: 'query', body: {layers: []}}},
        {...artifact(), envelope: {family: 'map', body: {format: 'honua_map_package.v1', mapSpec: {}}}},
        {...artifact(), family: 'dashboard', envelope: {family: 'dashboard', body: {dashboard: {widgets: []}}}},
        {...artifact(), family: 'app', envelope: {family: 'app', body: {layers: {}}}},
    ]) {
        const h = harness(() => publishedResponse(value));
        await h.viewer.reload();
        assert.match(h.status.textContent, /different viewer/);
        assert.equal(h.renders.length, 0);
    }
});

test('a missing source projection and missing partition stop before the renderer', async () => {
    const noCatalog = harness(() => publishedResponse({...artifact(), sourceCatalog: undefined}));
    await noCatalog.viewer.reload();
    assert.match(noCatalog.status.textContent, /source metadata is unavailable/);
    assert.equal(noCatalog.renders.length, 0);

    const noPartition = harness(() => publishedResponse(artifact(), 200, 'invalid'));
    await noPartition.viewer.reload();
    assert.match(noPartition.status.textContent, /viewer connection is unavailable/);
    assert.equal(noPartition.renders.length, 0);
});

test('reload reads a new version from the same publication URL and disposes the old canvas', async () => {
    let version = 0;
    const h = harness(() => publishedResponse(artifact(++version === 1 ? 'first' : 'second')));
    await h.viewer.reload();
    const first = h.renders[0].body;
    await h.viewer.reload();
    assert.deepEqual(h.calls.map(call => call.url), [publicationUrl, publicationUrl]);
    assert.equal(h.disposed, 1);
    assert.notEqual(h.renders[1].body, first);
    assert.equal(h.renders[1].body.version, 'second');
    assert.equal(h.title.textContent, 'Map second');
    h.viewer.dispose();
});

test('network failure on reload disposes the old map and shows an unavailable state', async () => {
    let fail = false;
    const h = harness(() => {
        if (fail) throw new Error('Network unavailable');
        return publishedResponse();
    });
    await h.viewer.reload();
    fail = true;
    await h.viewer.reload();
    assert.equal(h.disposed, 1);
    assert.equal(h.renders.length, 1);
    assert.match(h.status.textContent, /Network unavailable/);
    h.viewer.dispose();
});

test('late publication and renderer replies cannot replace a newer view', async () => {
    const slowPublication = deferred();
    let calls = 0;
    const h = harness(() => ++calls === 1 ? slowPublication.promise : publishedResponse(artifact('new')));
    const first = h.viewer.reload();
    await h.viewer.reload();
    slowPublication.resolve(publishedResponse(artifact('old')));
    await first;
    assert.equal(h.renders.length, 1);
    assert.equal(h.renders[0].body.version, 'new');
    assert.equal(h.title.textContent, 'Map new');
    h.viewer.dispose();

    const oldCanvas = deferred();
    const renderStarted = deferred();
    let version = 0;
    const q = harness(() => publishedResponse(artifact(++version === 1 ? 'old' : 'new')));
    let renderCount = 0;
    let disposedLate = 0;
    q.runtime.createReadOnlyCompositionView = options => {
        q.renders.push(options);
        if (++renderCount === 1) {
            renderStarted.resolve();
            return {dispose() { disposedLate++; }, settled: () => oldCanvas.promise, statusDetail: ''};
        }
        return {dispose() {}, settled: async () => {}, statusDetail: ''};
    };
    const pending = q.viewer.reload();
    await renderStarted.promise;
    await q.viewer.reload();
    oldCanvas.resolve();
    await pending;
    assert.equal(disposedLate, 1);
    assert.equal(q.title.textContent, 'Map new');
    q.viewer.dispose();
});

test('dispose aborts the active view and discards late canvas creation', async () => {
    const lateCanvas = deferred();
    const renderStarted = deferred();
    const h = harness();
    let disposedLate = 0;
    h.runtime.createReadOnlyCompositionView = () => {
        renderStarted.resolve();
        return {dispose() { disposedLate++; }, settled: () => lateCanvas.promise, statusDetail: ''};
    };
    const pending = h.viewer.reload();
    await renderStarted.promise;
    h.viewer.dispose();
    lateCanvas.resolve();
    await pending;
    assert.equal(disposedLate, 1);
    assert.equal(h.title.textContent, '');
    assert.equal(h.calls.length, 1);
    await h.viewer.reload();
    assert.equal(h.calls.length, 1);
});
