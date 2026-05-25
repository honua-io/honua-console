// Connections list / detail / create wizard

function ConnectionsList() {
  const rows = [
    { n:'prod-postgis', k:'PostgreSQL · PostGIS', host:'db-prod.honua.internal', tbl:142, st:'ok', sync:'2m' },
    { n:'s3-imagery', k:'S3 bucket', host:'s3://honua-imagery', tbl:'—', st:'ok', sync:'12m' },
    { n:'sql-legacy', k:'SQL Server', host:'sql.legacy.corp', tbl:21, st:'warn', sync:'3d' },
    { n:'s3-sensors', k:'S3 bucket', host:'s3://sensors-east', tbl:'—', st:'ok', sync:'4m' },
    { n:'snowflake-bi', k:'Snowflake', host:'honua.snowflakecomputing.com', tbl:74, st:'ok', sync:'8m' },
    { n:'fgdb-archive', k:'File geodatabase', host:'\\\\nas01\\archive', tbl:6, st:'bad', sync:'failed' },
  ];
  return (
    <div className="scr">
      <TopBar crumbs={['Connections']} />
      <Sidebar active="connections" />
      <div className="main">
        <PageHead
          title="Connections"
          sub={<span>Persistent credential-bound data stores. <span className="muted">Remote services (Esri, OGC API, WMS, CSW) aren't connections — bring those in via <a className="mono" style={{color:'var(--pencil)'}}>Imports → Remote service</a>. That's a <b>migration</b> path — a one-time copy off of someone else's server. Honua doesn't proxy, sync, or hold credentials.</span></span>}
          actions={<>
            <Btn>Test all</Btn>
            <Btn kind="p" ico="+">Add connection</Btn>
          </>}
        />
        <Toolbar
          filters={<>
            <FiltChip on x>kind: 4</FiltChip>
            <FiltChip>status: any</FiltChip>
            <FiltChip>last sync: any</FiltChip>
            <FiltChip>+ filter</FiltChip>
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>6 connections</span>
            <Btn ghost sm>Columns</Btn>
            <Btn ghost sm>Export</Btn>
          </>}
        />
        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th style={{width:24}}><input type="checkbox" readOnly /></th>
              <th>Name</th><th>Kind</th><th>Host / URI</th>
              <th className="num">Tables</th><th>Status</th><th>Last sync</th><th></th>
            </tr></thead>
            <tbody>
              {rows.map((r,i) => (
                <tr key={i} className={i===0 ? 'sel' : ''}>
                  <td><input type="checkbox" readOnly defaultChecked={i===0} /></td>
                  <td><b>{r.n}</b></td>
                  <td>{r.k}</td>
                  <td><span className="mono">{r.host}</span></td>
                  <td className="num">{r.tbl}</td>
                  <td>
                    {r.st === 'ok' && <Badge kind="ok">Connected</Badge>}
                    {r.st === 'warn' && <Badge kind="warn">Slow</Badge>}
                    {r.st === 'bad' && <Badge kind="bad">Failed</Badge>}
                  </td>
                  <td className="muted">{r.sync}</td>
                  <td><span className="muted">⋯</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function ConnectionDetail() {
  return (
    <div className="scr">
      <TopBar crumbs={['Connections','prod-postgis']} />
      <Sidebar active="connections" />
      <div className="main">
        <PageHead
          title="prod-postgis"
          sub={<span><Badge kind="ok">Connected</Badge> <span className="muted" style={{marginLeft:8}}>PostgreSQL 16 · PostGIS 3.4 · 142 tables · synced 2m ago</span></span>}
          actions={<><Btn>Test</Btn><Btn>Sync now</Btn><Btn kind="p">+ Resource from this</Btn></>}
        />
        <Tabs items={[
          { k:'overview', t:'Overview' },
          { k:'tables', t:'Tables', ct: 142 },
          { k:'resources', t:'Used by', ct: 38 },
          { k:'sched', t:'Schedule' },
          { k:'creds', t:'Credentials' },
          { k:'log', t:'Activity' },
        ]} active="tables" />

        <Toolbar
          filters={<>
            <FiltChip on x>schema: public</FiltChip>
            <FiltChip>geometry only</FiltChip>
            <FiltChip>has primary key</FiltChip>
            <input className="inp" style={{width:200, height:22}} placeholder="Filter 142 tables…" readOnly />
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>3 selected</span>
            <Btn kind="a" sm>+ Resource from selection</Btn>
          </>}
        />

        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th style={{width:24}}><input type="checkbox" readOnly /></th>
              <th>Schema.table</th><th>Geometry</th><th className="num">Rows</th>
              <th className="num">Cols</th><th>PK</th><th>In catalog</th><th>Last seen</th>
            </tr></thead>
            <tbody>
              {[
                { n:'public.parcels_2024', g:'MultiPolygon · 4326', r:'1,284,021', c:24, pk:'gid', in:'parcels_2024', s:'2m' },
                { n:'public.wetlands_2025', g:'MultiPolygon · 4326', r:'82,114', c:18, pk:'id', in:'wetlands_2025', s:'2m' },
                { n:'public.fire_perimeters', g:'MultiPolygon · 3857', r:'14,028', c:12, pk:'fid', in:'fire_perimeters', s:'2m' },
                { n:'public.fire_observations', g:'Point · 4326', r:'2.1M', c:9, pk:'id', in:<span className="muted">—</span>, s:'2m' },
                { n:'public.obs_stations', g:'Point · 4326', r:'4,210', c:14, pk:'id', in:'obs_stations', s:'2m' },
                { n:'public.watersheds_v3', g:'MultiPolygon · 4326', r:'18,442', c:21, pk:'id', in:'watersheds_v3', s:'2m' },
                { n:'public.land_cover_2024', g:'Polygon · 4326', r:'612,008', c:8, pk:'id', in:<span className="muted">—</span>, s:'2m' },
                { n:'public.air_quality_obs', g:'Point · 4326', r:'14.8M', c:6, pk:'id', in:<span className="muted">—</span>, s:'2m' },
                { n:'public.census_blocks', g:'MultiPolygon · 4269', r:'8.1M', c:32, pk:'geoid', in:'census_blocks', s:'2m' },
                { n:'public.coastline', g:'LineString · 4326', r:'48,002', c:5, pk:'id', in:<span className="muted">—</span>, s:'2m' },
              ].map((r,i) => (
                <tr key={i} className={i<3 ? 'sel' : ''}>
                  <td><input type="checkbox" readOnly defaultChecked={i<3} /></td>
                  <td><span className="mono">{r.n}</span></td>
                  <td><span className="tag">{r.g}</span></td>
                  <td className="num mono">{r.r}</td>
                  <td className="num">{r.c}</td>
                  <td className="mono">{r.pk}</td>
                  <td>{typeof r.in === 'string' ? <span style={{color:'var(--pencil)'}}>◇ {r.in}</span> : r.in}</td>
                  <td className="muted">{r.s}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function ConnectionWizard() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Connections','Add connection']} />
      <div className="wiz">
        <Stepper steps={['Kind','Reach','Credentials','Test','Review']} on={2} />
        <div className="body" style={{display:'grid',gridTemplateColumns:'1.4fr 1fr', gap:24}}>
          <div>
            <h2 style={{margin:'0 0 6px',font:'600 16px var(--ui)'}}>Sign in to the source</h2>
            <div className="muted" style={{marginBottom:16,fontSize:11.5}}>
              These credentials are stored encrypted. We use them on each refresh. Honua never exposes them to published services.
            </div>

            <div style={{display:'grid',gridTemplateColumns:'1fr 1fr', gap:14}}>
              <Field label="Connection name" hint="Lower-case, dashes ok. Visible to your team.">
                <Inp value="prod-postgis" />
              </Field>
              <Field label="Auth method">
                <Sel value="Username & password" />
              </Field>
              <Field label="Host">
                <Inp mono value="db-prod.honua.internal" />
              </Field>
              <Field label="Port">
                <Inp mono value="5432" />
              </Field>
              <Field label="Database">
                <Inp mono value="honua_geo" />
              </Field>
              <Field label="Default schema">
                <Sel value="public" />
              </Field>
              <Field label="Username">
                <Inp mono value="honua_reader" />
              </Field>
              <Field label="Password">
                <Inp mono value="••••••••••••" />
              </Field>
            </div>

            <div style={{marginTop:6}}>
              <Field label="Network">
                <div className="row" style={{gap:14, fontSize:11.5}}>
                  <label><input type="radio" readOnly defaultChecked /> Direct</label>
                  <label><input type="radio" readOnly /> Via SSH tunnel</label>
                  <label><input type="radio" readOnly /> Via private link</label>
                </div>
              </Field>
              <Field label="Allow Honua to read">
                <div className="row" style={{gap:14,fontSize:11.5,flexWrap:'wrap'}}>
                  <label><input type="checkbox" readOnly defaultChecked /> Table metadata</label>
                  <label><input type="checkbox" readOnly defaultChecked /> Row counts &amp; extents</label>
                  <label><input type="checkbox" readOnly defaultChecked /> Sample rows</label>
                  <label><input type="checkbox" readOnly /> Spatial indexes</label>
                </div>
              </Field>
            </div>
          </div>

          <div className="col">
            <Callout kind="info">
              <b>Heads up.</b> Connections are read-only by default. To let Honua write back (e.g. write tile caches), enable it in advanced settings after the connection is created.
            </Callout>
            <div className="card">
              <h3>What we'll do next</h3>
              <ol style={{margin:'4px 0 0 16px',padding:0,fontSize:11.5,lineHeight:1.6}}>
                <li>Test the connection.</li>
                <li>Scan tables &amp; views — read only.</li>
                <li>Suggest candidate resources you can publish.</li>
              </ol>
            </div>
            <Ann>password fields are masked, never echoed back to logs.</Ann>
            <Ann red>add "use existing secret" picker when secret-mgr integration ships.</Ann>
          </div>
        </div>
        <div className="foot">
          <Btn ghost>← Back</Btn>
          <div className="row">
            <Btn ghost>Save draft</Btn>
            <Btn kind="p">Test connection →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function EsriConnectionDetail() {
  // Tree on left (folders -> services -> layers), detail on right (selected layer).
  const T = ({ depth = 0, icon, name, meta, on, open, tone }) => (
    <div className="row" style={{
      padding: '3px 8px',
      paddingLeft: 8 + depth * 14,
      fontSize: 11.5,
      background: on ? '#fffae0' : 'transparent',
      borderLeft: on ? '3px solid var(--ink)' : '3px solid transparent',
      borderBottom: '1px solid #f1f1f1',
      cursor: 'pointer',
    }}>
      <span style={{ width: 10, color: '#999', textAlign: 'center', fontSize: 9 }}>
        {open === true ? '▾' : open === false ? '▸' : ' '}
      </span>
      <span style={{ width: 14, textAlign: 'center', color: tone || '#666' }}>{icon}</span>
      <span style={{ flex: 1, fontWeight: on ? 600 : 400, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{name}</span>
      {meta && <span className="muted mono" style={{ fontSize: 9.5 }}>{meta}</span>}
    </div>
  );

  return (
    <div className="scr">
      <TopBar crumbs={['Connections', 'esri-online']} />
      <Sidebar active="connections" />
      <div className="main">
        <PageHead
          title="esri-online"
          sub={<span><Badge kind="ok">Connected</Badge> <span className="muted" style={{ marginLeft: 8 }}>ArcGIS Online · org.maps.arcgis.com · 24 services · 96 layers · synced 1h ago</span></span>}
          actions={<><Btn>Test</Btn><Btn>Sync now</Btn><Btn kind="p">+ Resource from selection</Btn></>}
        />
        <Tabs items={[
          { k: 'overview', t: 'Overview' },
          { k: 'browse', t: 'Browse', ct: '24 svc · 96 lyr' },
          { k: 'resources', t: 'Used by', ct: 12 },
          { k: 'creds', t: 'Credentials' },
          { k: 'log', t: 'Activity' },
        ]} active="browse" />

        <Toolbar
          filters={<>
            <FiltChip on x>type: FeatureServer, MapServer</FiltChip>
            <FiltChip>has-geometry</FiltChip>
            <FiltChip>not in catalog</FiltChip>
            <input className="inp" style={{ width: 220, height: 22 }} placeholder="Filter services & layers…" readOnly />
          </>}
          right={<>
            <span className="muted" style={{ fontSize: 11 }}>3 layers selected</span>
            <Btn kind="a" sm>+ Resource from selection</Btn>
          </>}
        />

        {/* Two-pane: tree + detail */}
        <div style={{ display: 'grid', gridTemplateColumns: '380px 1fr', flex: 1, overflow: 'hidden' }}>
          {/* TREE */}
          <div style={{ borderRight: '1px solid #e4e4e4', overflow: 'auto', background: '#fafafa' }}>
            <div style={{ padding: '6px 10px', fontSize: 10, color: '#888', textTransform: 'uppercase', letterSpacing: '0.06em', background: '#f1f1f1', borderBottom: '1px solid #e0e0e0' }}>
              org.maps.arcgis.com / state-gis
            </div>

            {/* root */}
            <T depth={0} icon="▤" name="(root)" meta="3 svc · 0 lyr" open={true} />

            {/* folder: Cadastre */}
            <T depth={1} icon="🗀" name="Cadastre" meta="2 svc · 8 lyr" open={true} />
              <T depth={2} icon="◈" name="Parcels" meta="FeatureServer · 3 lyr" open={true} on={false} />
                <T depth={3} icon="◇" name="0 · Parcels" meta="MultiPolygon · 1.28M" tone="var(--pencil)" />
                <T depth={3} icon="◇" name="1 · Parcel centroids" meta="Point · 1.28M" tone="var(--pencil)" on={true} />
                <T depth={3} icon="▦" name="2 · Tax assessment events" meta="Table · 3.4M" tone="var(--pencil)" />
              <T depth={2} icon="◈" name="Parcels (historic)" meta="MapServer · 5 lyr" open={false} />

            {/* folder: Environment */}
            <T depth={1} icon="🗀" name="Environment" meta="4 svc · 18 lyr" open={true} />
              <T depth={2} icon="◈" name="Wetlands" meta="FeatureServer · 1 lyr · public" open={true} />
                <T depth={3} icon="◇" name="0 · Wetland polygons" meta="MultiPolygon · 82k" tone="var(--pencil)" />
              <T depth={2} icon="◈" name="Watersheds_v3" meta="FeatureServer · 2 lyr" open={false} />
              <T depth={2} icon="◈" name="Sentinel-2 imagery" meta="ImageServer · raster" open={false} />
              <T depth={2} icon="◈" name="Land cover 2024" meta="ImageServer · raster" open={false} />

            {/* folder: Hazards */}
            <T depth={1} icon="🗀" name="Hazards" meta="3 svc · 11 lyr" open={false} />

            {/* folder: Infrastructure */}
            <T depth={1} icon="🗀" name="Infrastructure" meta="6 svc · 28 lyr" open={false} />

            {/* folder: Reference */}
            <T depth={1} icon="🗀" name="Reference" meta="5 svc · 22 lyr" open={false} />

            {/* unpublished items folder (not in nav, surfaced as a virtual group) */}
            <div style={{ padding: '8px 10px 4px', fontSize: 9.5, color: '#888', textTransform: 'uppercase', letterSpacing: '0.08em' }}>Other</div>
            <T depth={1} icon="◈" name="Org search index" meta="GeoSearchServer" open={false} />
            <T depth={1} icon="◈" name="Tiles · base map" meta="VectorTileServer" open={false} />

            <div style={{ padding: '10px 12px', borderTop: '1px dashed #d8d8d8', fontSize: 10.5, color: '#888' }}>
              Tip · check a layer to add it to selection. Folders &amp; services aren't importable on their own.
            </div>
          </div>

          {/* DETAIL */}
          <div style={{ overflow: 'auto' }}>
            <div style={{ padding: '12px 18px', borderBottom: '1px solid #e4e4e4' }}>
              <div className="muted" style={{ fontSize: 10.5 }}>Cadastre / Parcels / layer 1</div>
              <div className="row" style={{ marginTop: 2 }}>
                <h2 style={{ margin: 0, font: '600 16px var(--ui)' }}>Parcel centroids</h2>
                <span className="tag">FeatureServer · layer 1</span>
                <Badge kind="accent">selected</Badge>
                <div style={{ flex: 1 }} />
                <Btn ghost sm>View REST ↗</Btn>
                <Btn sm>+ Resource from this layer</Btn>
              </div>
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                Centroids of statewide parcels. Published by state-gis. Last edited 2026-03-14.
              </div>
            </div>

            <div style={{ padding: '12px 18px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
              <div className="card" style={{ gap: 6 }}>
                <h3>Service properties</h3>
                <dl className="kv">
                  <dt>Type</dt><dd>FeatureServer</dd>
                  <dt>Service URL</dt><dd className="mono" style={{ fontSize: 10 }}>…/Cadastre/Parcels/FeatureServer</dd>
                  <dt>Spatial reference</dt><dd className="mono">EPSG:4326</dd>
                  <dt>Capabilities</dt>
                  <dd>
                    <span className="tag">Query</span> <span className="tag">Extract</span> <span className="tag" style={{opacity:0.4}}>Create</span> <span className="tag" style={{opacity:0.4}}>Update</span>
                  </dd>
                  <dt>Anonymous?</dt><dd><Badge kind="ok">yes (public)</Badge></dd>
                  <dt>Owner</dt><dd>state-gis</dd>
                </dl>
              </div>
              <div className="card" style={{ gap: 6 }}>
                <h3>Layer 1 · facts</h3>
                <dl className="kv">
                  <dt>Geometry</dt><dd>Point · 4326</dd>
                  <dt>Feature count</dt><dd className="mono">1,284,021</dd>
                  <dt>Fields</dt><dd>9</dd>
                  <dt>Object ID</dt><dd className="mono">objectid</dd>
                  <dt>Display field</dt><dd className="mono">parcel_id</dd>
                  <dt>Time-aware</dt><dd>no</dd>
                  <dt>Has attachments</dt><dd>no</dd>
                  <dt>Extent</dt><dd className="mono" style={{ fontSize: 10 }}>-124.4 32.5 / -114.1 42.0</dd>
                </dl>
              </div>
            </div>

            <div style={{ padding: '0 18px 12px' }}>
              <div className="card" style={{ padding: 0 }}>
                <div style={{ padding: '8px 12px', borderBottom: '1px solid #e4e4e4', display: 'flex', alignItems: 'center' }}>
                  <h3>Fields · 9</h3>
                  <span className="muted" style={{ fontSize: 11, marginLeft: 8 }}>read from REST, will become resource fields on import</span>
                  <div style={{ flex: 1 }} />
                  <Btn ghost sm>Preview rows</Btn>
                </div>
                <table className="tbl tbl--cmpt">
                  <thead><tr>
                    <th>Field</th><th>Alias</th><th>Type</th><th>Domain</th><th>Nullable</th><th>Notes</th>
                  </tr></thead>
                  <tbody>
                    <tr><td className="mono"><b>objectid</b></td><td>OBJECTID</td><td className="mono">esriFieldTypeOID</td><td className="muted">—</td><td>no</td><td><Badge kind="accent">Primary ID</Badge></td></tr>
                    <tr><td className="mono">parcel_id</td><td>Parcel ID</td><td className="mono">String(32)</td><td className="muted">—</td><td>no</td><td><Badge>Display</Badge></td></tr>
                    <tr><td className="mono">use_code</td><td>Use code</td><td className="mono">String(8)</td><td>landuse · 12</td><td>yes</td><td className="muted">coded values</td></tr>
                    <tr><td className="mono">area_m2</td><td>Area (m²)</td><td className="mono">Double</td><td className="muted">—</td><td>yes</td><td></td></tr>
                    <tr><td className="mono">owner_name</td><td>Owner</td><td className="mono">String(120)</td><td className="muted">—</td><td>yes</td><td><Badge kind="warn">PII suggested</Badge></td></tr>
                    <tr><td className="mono">last_assessment</td><td>Last assessment</td><td className="mono">Date</td><td className="muted">—</td><td>yes</td><td className="muted">temporal candidate</td></tr>
                    <tr><td className="mono">assessed_value</td><td>Assessed value (USD)</td><td className="mono">Double</td><td className="muted">—</td><td>yes</td><td></td></tr>
                    <tr><td className="mono">created_user</td><td>Created by</td><td className="mono">String(64)</td><td className="muted">—</td><td>yes</td><td className="muted">audit</td></tr>
                    <tr><td className="mono">created_date</td><td>Created on</td><td className="mono">Date</td><td className="muted">—</td><td>yes</td><td className="muted">audit</td></tr>
                  </tbody>
                </table>
              </div>
            </div>

            <div style={{ padding: '0 18px 14px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
              <div className="card">
                <h3>Relationships &amp; related tables</h3>
                <table className="tbl tbl--cmpt">
                  <thead><tr><th>Name</th><th>Role</th><th>Related to</th></tr></thead>
                  <tbody>
                    <tr><td>parcel_to_owner</td><td>origin</td><td className="mono">owner_lookup (table)</td></tr>
                    <tr><td>parcel_to_events</td><td>origin</td><td className="mono">layer 2 · Tax assessment events</td></tr>
                  </tbody>
                </table>
                <Ann>imports honour relationships when you select all related layers together.</Ann>
              </div>
              <div className="card">
                <h3>Renderer hints</h3>
                <div className="row" style={{ gap: 14 }}>
                  <div style={{ width: 56, height: 56, border: '1px solid #ccc', borderRadius: 4, background: 'radial-gradient(circle at 50% 50%, #d9a23a 0 18%, transparent 18%) no-repeat, #fff' }} />
                  <div style={{ flex: 1, fontSize: 11 }}>
                    <div className="row"><span className="muted" style={{ flex: 1 }}>Renderer</span><span>simpleMarker</span></div>
                    <div className="row"><span className="muted" style={{ flex: 1 }}>Symbol</span><span>circle 6px · #d9a23a</span></div>
                    <div className="row"><span className="muted" style={{ flex: 1 }}>Scale visible</span><span className="mono">1:500 — 1:5M</span></div>
                  </div>
                </div>
                <Callout kind="info">Honua will translate this on import: → resource's default Style on the Presentation tab, with each publish target (WMS, MapServer) inheriting unless overridden.</Callout>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ConnectionsList, ConnectionDetail, EsriConnectionDetail, ConnectionWizard });
