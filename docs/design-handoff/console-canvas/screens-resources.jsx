// Data Resources list (A: table, B: grouped) + Resource detail tabs

function ResourcesListA() {
  const rows = [
    { n:'parcels_2024', t:'Polygon', src:'prod-postgis', pub:4, st:'ok', ow:'jamie', upd:'2m' },
    { n:'wetlands_2025', t:'Polygon', src:'prod-postgis', pub:3, st:'ok', ow:'jamie', upd:'14m' },
    { n:'fire_perimeters', t:'Polygon', src:'prod-postgis', pub:2, st:'warn', ow:'k.tan', upd:'28m' },
    { n:'fire_observations', t:'Point', src:'prod-postgis', pub:0, st:'draft', ow:'jamie', upd:'1h' },
    { n:'obs_stations', t:'Point', src:'prod-postgis', pub:3, st:'ok', ow:'k.tan', upd:'1h' },
    { n:'watersheds_v3', t:'Polygon', src:'prod-postgis', pub:1, st:'bad', ow:'k.tan', upd:'2h' },
    { n:'land_cover_2024', t:'Raster', src:'s3-imagery', pub:2, st:'ok', ow:'system', upd:'4h' },
    { n:'sentinel_2_tiles', t:'Raster', src:'s3-imagery', pub:1, st:'ok', ow:'system', upd:'4h' },
    { n:'census_blocks', t:'Polygon', src:'snowflake-bi', pub:2, st:'ok', ow:'a.lee', upd:'1d' },
    { n:'noaa_wms_layers', t:'Service', src:'wms-noaa', pub:1, st:'ok', ow:'system', upd:'1d' },
    { n:'air_quality_obs', t:'Point', src:'s3-sensors', pub:2, st:'ok', ow:'k.tan', upd:'2d' },
    { n:'roads_osm', t:'Line', src:'prod-postgis', pub:3, st:'ok', ow:'jamie', upd:'3d' },
  ];
  return (
    <div className="scr">
      <TopBar crumbs={['Data resources']} />
      <Sidebar active="resources" />
      <div className="main">
        <PageHead
          title="Data resources"
          sub="The catalog. One resource = one canonical dataset that can be published to many formats."
          actions={<>
            <Btn>Import remote service</Btn>
            <Btn>+ From file</Btn>
            <Btn kind="p">+ From table</Btn>
          </>}
        />
        <Toolbar
          filters={<>
            <FiltChip on x>type: any</FiltChip>
            <FiltChip>source: prod-postgis</FiltChip>
            <FiltChip>published: any</FiltChip>
            <FiltChip>owner: me</FiltChip>
            <FiltChip>+ filter</FiltChip>
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>128 resources · 12 shown</span>
            <Btn ghost sm>Group by</Btn>
            <Btn ghost sm>Columns</Btn>
          </>}
        />
        <div style={{overflow:'auto',flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th style={{width:24}}><input type="checkbox" readOnly /></th>
              <th>Name</th>
              <th>Type</th>
              <th>Source</th>
              <th className="num">Published</th>
              <th>Status</th>
              <th>Owner</th>
              <th>Updated</th>
              <th style={{width:120}}>Actions</th>
            </tr></thead>
            <tbody>
              {rows.map((r,i) => (
                <tr key={i}>
                  <td><input type="checkbox" readOnly /></td>
                  <td><span style={{color:'var(--pencil)'}}>◇</span> <b>{r.n}</b></td>
                  <td><span className="tag">{r.t}</span></td>
                  <td><span className="mono">{r.src}</span></td>
                  <td className="num">
                    {r.pub > 0
                      ? <span><b>{r.pub}</b><span className="muted"> / 5</span></span>
                      : <span className="muted">—</span>}
                  </td>
                  <td>
                    {r.st === 'ok' && <Badge kind="ok">Published</Badge>}
                    {r.st === 'warn' && <Badge kind="warn">Needs review</Badge>}
                    {r.st === 'bad' && <Badge kind="bad">Blocked</Badge>}
                    {r.st === 'draft' && <Badge kind="draft">Draft</Badge>}
                  </td>
                  <td className="mono">{r.ow}</td>
                  <td className="muted">{r.upd}</td>
                  <td>
                    <span className="muted" style={{fontSize:11}}>Open · Publish · ⋯</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function ResourcesListB() {
  // Variant: grouped by source, card-ish rows
  const groups = [
    { src:'prod-postgis', tag:'PostgreSQL', n: 38, rows: [
      { n:'parcels_2024', t:'Polygon', pub: [<Badge key="f" kind="ok">Features</Badge>,<Badge key="s" kind="ok">STAC</Badge>,<Badge key="t" kind="ok">Tiles</Badge>,<Badge key="c" kind="warn">CSW</Badge>] },
      { n:'wetlands_2025', t:'Polygon', pub: [<Badge key="f" kind="ok">Features</Badge>,<Badge key="s" kind="ok">STAC</Badge>,<Badge key="t" kind="ok">Tiles</Badge>] },
      { n:'fire_perimeters', t:'Polygon', pub: [<Badge key="f" kind="ok">Features</Badge>,<Badge key="t" kind="warn">Tiles</Badge>] },
      { n:'fire_observations', t:'Point', pub: [<Badge key="d" kind="draft">Draft</Badge>] },
    ]},
    { src:'s3-imagery', tag:'S3', n: 14, rows: [
      { n:'land_cover_2024', t:'Raster', pub: [<Badge key="t" kind="ok">Tiles</Badge>,<Badge key="s" kind="ok">STAC</Badge>] },
      { n:'sentinel_2_tiles', t:'Raster', pub: [<Badge key="t" kind="ok">Tiles</Badge>] },
    ]},
    { src:'s3-sensors', tag:'S3', n: 7, rows: [
      { n:'air_quality_obs', t:'Point', pub: [<Badge key="f" kind="ok">Features</Badge>,<Badge key="t" kind="ok">Tiles</Badge>] },
    ]},
  ];
  return (
    <div className="scr">
      <TopBar crumbs={['Data resources']} />
      <Sidebar active="resources" />
      <div className="main">
        <PageHead
          title="Data resources"
          sub="Grouped by source · sketch of how a denser browser could feel"
          actions={<><Btn>Switch to table</Btn><Btn kind="p">+ From table</Btn></>}
        />
        <Toolbar
          filters={<>
            <FiltChip on x>group: source</FiltChip>
            <FiltChip>type: any</FiltChip>
            <FiltChip>+ filter</FiltChip>
          </>}
          right={<span className="muted" style={{fontSize:11}}>128 across 14 sources</span>}
        />
        <div style={{overflow:'auto', flex:1, padding:'8px 18px 18px'}}>
          {groups.map(g => (
            <div key={g.src} style={{marginBottom:14}}>
              <div className="row" style={{padding:'6px 0',fontSize:11}}>
                <span>▾</span>
                <b>{g.src}</b>
                <span className="tag">{g.tag}</span>
                <span className="muted" style={{marginLeft:6}}>{g.n} resources</span>
                <div style={{flex:1}}/>
                <Btn ghost sm>+ from this</Btn>
              </div>
              <div style={{border:'1px solid #e4e4e4', borderRadius:6, overflow:'hidden'}}>
                <table className="tbl tbl--cmpt">
                  <thead><tr>
                    <th>Resource</th><th>Type</th><th>Published to</th><th className="num">Features</th><th>Updated</th>
                  </tr></thead>
                  <tbody>
                    {g.rows.map((r,i) => (
                      <tr key={i}>
                        <td><span style={{color:'var(--pencil)'}}>◇</span> <b>{r.n}</b></td>
                        <td><span className="tag">{r.t}</span></td>
                        <td><div className="row" style={{gap:4,flexWrap:'wrap'}}>{r.pub}</div></td>
                        <td className="num mono">—</td>
                        <td className="muted">recent</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/* ---------- Resource detail shell ---------- */

const RES_SUPER = [
  { k:'define', t:'Define', sub:['Overview','Source','Fields','Metadata','Presentation','Advanced'] },
  { k:'publish', t:'Publish', sub:['Publish','Access'] },
  { k:'operate', t:'Operate', sub:['Validation','Activity'] },
];

function ResHead({ status='ok' }) {
  return (
    <div style={{padding:'12px 18px 0'}}>
      <div className="row" style={{marginBottom:4}}>
        <div className="muted" style={{fontSize:11}}>
          Data resources <span style={{color:'#bbb'}}>/</span> prod-postgis <span style={{color:'#bbb'}}>/</span>
        </div>
      </div>
      <div className="row">
        <h1 style={{margin:0,font:'600 18px var(--ui)',letterSpacing:'-0.01em'}}>
          <span style={{color:'var(--pencil)'}}>◇</span> parcels_2024
        </h1>
        {status === 'ok' && <Badge kind="ok" lg>Published</Badge>}
        {status === 'warn' && <Badge kind="warn" lg>Needs review</Badge>}
        {status === 'bad' && <Badge kind="bad" lg>Blocked</Badge>}
        <span className="muted" style={{fontSize:11}}>v4 · last published 2m ago by jamie</span>
        <div style={{flex:1}}/>
        <Btn ghost>Preview</Btn>
        <Btn>Run validation</Btn>
        <Btn kind="p">Publish…</Btn>
      </div>
      <div className="muted" style={{fontSize:11.5, marginTop:6}}>
        Tax parcels for FY 2024. Refreshed nightly from prod-postgis. Sealed boundary.
      </div>
    </div>
  );
}

function SuperTabs({ on, sub }) {
  return (
    <div>
      <div className="supertabs" style={{marginTop:8}}>
        {RES_SUPER.map(g => (
          <div key={g.k} className={'stab' + (g.k === on ? ' on' : '')}>
            {g.t}
            <span className="muted" style={{fontWeight:400,fontSize:9.5}}>· {g.sub.length}</span>
          </div>
        ))}
        <div style={{flex:1}}/>
      </div>
      <Tabs sub items={RES_SUPER.find(g => g.k === on).sub.map(t => ({ k: t.toLowerCase(), t }))} active={sub} />
    </div>
  );
}

function ResOverview() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="define" sub="overview" />
        <div className="detail">
          <div className="col">
            <div className="card">
              <h3>Description</h3>
              <div style={{fontSize:11.5}}>Tax parcels for FY 2024 covering 14 counties. Source is the canonical assessor table; geometry is sealed to 1 mm precision before publishing.</div>
            </div>

            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3>Preview</h3>
              </div>
              <div style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', minHeight:240}}>
                <Ph style={{borderRadius:0, borderRight:'1px dashed #c5c5c5', border:0, borderRight:'1px dashed #c5c5c5', background:'repeating-linear-gradient(135deg,#f3f3f3 0 6px,#e8e8e8 6px 12px)'}}>map preview · 4326</Ph>
                <div style={{padding:'8px 12px', fontSize:11}}>
                  <div className="row" style={{marginBottom:6}}><b>Sample rows</b><span className="muted" style={{marginLeft:'auto',fontSize:10}}>5 of 1.2M</span></div>
                  <table className="tbl tbl--cmpt" style={{fontSize:10}}>
                    <thead><tr><th>gid</th><th>parcel_id</th><th>area_m2</th></tr></thead>
                    <tbody>
                      <tr><td>1</td><td className="mono">04-021-118</td><td className="num mono">2,148</td></tr>
                      <tr><td>2</td><td className="mono">04-021-119</td><td className="num mono">1,902</td></tr>
                      <tr><td>3</td><td className="mono">04-021-120</td><td className="num mono">3,012</td></tr>
                      <tr><td>4</td><td className="mono">04-021-121</td><td className="num mono">2,008</td></tr>
                      <tr><td>5</td><td className="mono">04-021-122</td><td className="num mono">1,847</td></tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>

            <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
              <div className="card">
                <h3>Where it's published</h3>
                <div className="col" style={{gap:4}}>
                  {[
                    ['OGC API Features','v4','ok'],
                    ['STAC catalog','v4','ok'],
                    ['Tile service','v4','ok'],
                    ['CSW catalog','—','warn'],
                  ].map(([k,v,s]) => (
                    <div key={k} className="row" style={{fontSize:11, padding:'3px 0', borderBottom:'1px dashed #eee'}}>
                      <span style={{flex:1}}>{k}</span>
                      <span className="muted mono" style={{fontSize:10}}>{v}</span>
                      {s === 'ok' ? <Badge kind="ok">live</Badge> : <Badge kind="warn">stale</Badge>}
                    </div>
                  ))}
                </div>
              </div>
              <div className="card">
                <h3>Lineage</h3>
                <div style={{fontSize:11, lineHeight:1.6}}>
                  <div className="mono">prod-postgis · public.parcels_2024</div>
                  <div style={{paddingLeft:8,color:'#888'}}>↓ refresh nightly · 02:00 UTC</div>
                  <div className="mono">honua / parcels_2024 <span style={{color:'var(--pencil)'}}>(here)</span></div>
                  <div style={{paddingLeft:8,color:'#888'}}>↓ publish</div>
                  <div className="mono">→ 4 service targets</div>
                </div>
              </div>
            </div>
          </div>

          <div className="col">
            <div className="card" style={{gap:6}}>
              <h3>Quick facts</h3>
              <dl className="kv">
                <dt>Type</dt><dd>Polygon · MultiPolygon</dd>
                <dt>CRS</dt><dd className="mono">EPSG:4326</dd>
                <dt>Features</dt><dd className="mono">1,284,021</dd>
                <dt>Storage</dt><dd>1.4 GB</dd>
                <dt>Extent</dt><dd className="mono" style={{fontSize:10.5}}>-124.4 32.5<br/>-114.1 42.0</dd>
                <dt>Updated</dt><dd>02:00 UTC nightly</dd>
                <dt>Owner</dt><dd>jamie</dd>
                <dt>Licence</dt><dd>CC-BY 4.0</dd>
              </dl>
            </div>
            <div className="card">
              <h3>Tags</h3>
              <div className="row" style={{flexWrap:'wrap',gap:4}}>
                {['parcels','cadastre','assessor','FY2024','canonical'].map(t => <span key={t} className="tag">#{t}</span>)}
              </div>
            </div>
            <Ann>Overview is read-only summary. all editing happens in the sub-tabs.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResFields() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead status="warn" />
        <SuperTabs on="define" sub="fields" />

        <Toolbar
          filters={<>
            <FiltChip on x>scope: published</FiltChip>
            <FiltChip>type: any</FiltChip>
            <FiltChip>has issue</FiltChip>
            <input className="inp" style={{width:200, height:22}} placeholder="Filter 24 fields…" readOnly />
          </>}
          right={<>
            <Btn ghost sm>Auto-detect</Btn>
            <Btn ghost sm>Import schema</Btn>
            <Btn kind="p" sm>+ Field</Btn>
          </>}
        />
        <div style={{overflow:'auto', flex:1}}>
          <div className="field-row h">
            <span></span>
            <span>Field</span><span>Type</span><span>Format</span><span>Required</span><span>Indexed</span><span>Publish</span><span></span>
          </div>
          {[
            { f:'gid', t:'int8', fmt:'identifier', req:true, idx:true, pub:true, pk:true },
            { f:'parcel_id', t:'string', fmt:'cadastre-id', req:true, idx:true, pub:true },
            { f:'owner_name', t:'string', fmt:'name', req:false, idx:false, pub:false, note:'redacted in public' },
            { f:'area_m2', t:'float8', fmt:'meters²', req:true, idx:false, pub:true },
            { f:'use_code', t:'string', fmt:'enum (12)', req:true, idx:true, pub:true },
            { f:'assessed_value', t:'numeric', fmt:'USD', req:false, idx:false, pub:true },
            { f:'last_assessment', t:'date', fmt:'iso-date', req:true, idx:false, pub:true },
            { f:'geom', t:'geometry', fmt:'MultiPolygon · 4326', req:true, idx:true, pub:true, issue:'CRS not set on 2 records' },
          ].map((f,i) => (
            <div key={i} className="field-row" style={f.issue ? {background:'#fff7e6'} : null}>
              <span className="grip">⋮⋮</span>
              <span>
                {f.pk && <span className="pk" title="primary">★</span>} <b>{f.f}</b>
                {f.note && <span className="muted" style={{marginLeft:6,fontSize:10}}>{f.note}</span>}
                {f.issue && <span style={{marginLeft:6}}><Badge kind="warn">{f.issue}</Badge></span>}
              </span>
              <span className="mono">{f.t}</span>
              <span className="muted" style={{fontSize:10.5}}>{f.fmt}</span>
              <span>{f.req ? '✓' : <span className="muted">—</span>}</span>
              <span>{f.idx ? '✓' : <span className="muted">—</span>}</span>
              <span><input type="checkbox" readOnly defaultChecked={f.pub} /></span>
              <span className="muted" style={{textAlign:'right'}}>⋯</span>
            </div>
          ))}
        </div>
        <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', display:'flex',alignItems:'center',gap:8, background:'#fffae0'}}>
          <Badge kind="warn">1 issue</Badge>
          <span style={{fontSize:11}}>geom has 2 records missing CRS — this blocks publish to OGC Features.</span>
          <div style={{flex:1}}/>
          <Btn sm>View affected rows</Btn>
          <Btn kind="p" sm>Set CRS</Btn>
        </div>
      </div>
    </div>
  );
}

function ResPublish() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="publish" sub="publish" />
        <div className="detail">
          <div className="col">
            <Callout kind="info">
              <b>parcels_2024 is the canonical home.</b> Below are the {' '}
              <b>service / layer slots</b> where it's exposed. Catalog entries are <b>opt-in but default on</b> for the formats that have one: Esri service → Esri catalog, OGC API Features → OGC Records. Each row has a direct link out. <span className="muted">DCAT &amp; STAC publishing happens in Honua Console.</span>
            </Callout>

            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
                <h3>Service / layer slots · 4</h3>
                <span className="muted" style={{fontSize:11,marginLeft:8}}>where this resource is exposed as a live endpoint</span>
                <div style={{flex:1}}/>
                <Btn sm>+ Publish to service…</Btn>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th>Format</th>
                  <th>Service / layer slot</th>
                  <th>Slot label</th>
                  <th>Fields</th>
                  <th>Status</th>
                  <th>Catalog entry</th>
                  <th>Updated</th>
                  <th style={{width:80}}></th>
                </tr></thead>
                <tbody>
                  <tr>
                    <td>GeoServices FeatureServer</td>
                    <td className="mono">public-works-fs / layer 0</td>
                    <td className="mono">Parcels</td>
                    <td className="mono">23 / 24</td>
                    <td><Badge kind="ok">Live · v4</Badge></td>
                    <td>
                      <label className="row" style={{gap:4, fontSize:10.5}}>
                        <input type="checkbox" readOnly defaultChecked />
                        <a style={{color:'var(--pencil)', textDecoration:'underline dotted'}}>Esri catalog ↗</a>
                      </label>
                    </td>
                    <td className="muted">2m</td>
                    <td><a style={{cursor:'pointer',fontSize:10.5}}>Edit · ⋯</a></td>
                  </tr>
                  <tr>
                    <td>GeoServices MapServer</td>
                    <td className="mono">public-works-ms / layer 2</td>
                    <td className="mono">Parcels</td>
                    <td className="mono">12 / 24</td>
                    <td><Badge kind="ok">Live · v4</Badge></td>
                    <td>
                      <label className="row" style={{gap:4, fontSize:10.5}}>
                        <input type="checkbox" readOnly defaultChecked />
                        <a style={{color:'var(--pencil)', textDecoration:'underline dotted'}}>Esri catalog ↗</a>
                      </label>
                      <span className="muted mono" style={{fontSize:10}}>same entry as FS/0</span>
                    </td>
                    <td className="muted">2m</td>
                    <td><a style={{cursor:'pointer',fontSize:10.5}}>Edit · ⋯</a></td>
                  </tr>
                  <tr>
                    <td>OGC API Features</td>
                    <td className="mono">features-public / collections / parcels_2024</td>
                    <td className="mono">parcels_2024</td>
                    <td className="mono">23 / 24</td>
                    <td><Badge kind="ok">Live · v4</Badge></td>
                    <td>
                      <label className="row" style={{gap:4, fontSize:10.5}}>
                        <input type="checkbox" readOnly defaultChecked />
                        <a style={{color:'var(--pencil)', textDecoration:'underline dotted'}}>OGC Records ↗</a>
                      </label>
                    </td>
                    <td className="muted">2m</td>
                    <td><a style={{cursor:'pointer',fontSize:10.5}}>Edit · ⋯</a></td>
                  </tr>
                  <tr>
                    <td>WMTS</td>
                    <td className="mono">tiles-public / parcels_2024</td>
                    <td className="mono">parcels_2024</td>
                    <td className="mono">pyramid 0–14</td>
                    <td><Badge kind="warn">Stale · v3</Badge></td>
                    <td className="muted">— no catalog for WMTS</td>
                    <td className="muted">3d</td>
                    <td><a style={{cursor:'pointer',fontSize:10.5}}>Republish · ⋯</a></td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div className="card">
              <div className="row">
                <h3 style={{flex:1}}>Catalog registration · opt-in</h3>
                <span className="muted" style={{fontSize:11}}>independent of service publishing</span>
              </div>
              <div className="muted" style={{fontSize:11, marginBottom:8}}>
                Some catalogs describe a resource rather than a specific service. Opt in per catalog. Server-wide endpoint must be enabled in <a className="mono" style={{color:'var(--pencil)'}}>Settings → Catalog endpoints</a>.
              </div>
              <div className="col" style={{gap:6}}>
                <label className="row" style={{gap:8, padding:'6px 10px', border:'1px solid #d8d8d8', borderRadius:4, cursor:'pointer'}}>
                  <input type="checkbox" readOnly defaultChecked />
                  <div style={{flex:1}}>
                    <div style={{fontSize:11.5,fontWeight:600}}>Register in DCAT</div>
                    <div className="muted" style={{fontSize:10.5}}>open-data catalog · pulls license, publisher, distributions from canonical metadata</div>
                  </div>
                  <span className="tag" style={{cursor:'pointer'}}>Open in DCAT ↗</span>
                </label>
                <label className="row" style={{gap:8, padding:'6px 10px', border:'1px solid #d8d8d8', borderRadius:4, cursor:'pointer', opacity:0.7}}>
                  <input type="checkbox" readOnly />
                  <div style={{flex:1}}>
                    <div style={{fontSize:11.5,fontWeight:600}}>Register in STAC</div>
                    <div className="muted" style={{fontSize:10.5}}>spatio-temporal asset catalog · best for raster · server endpoint currently OFF</div>
                  </div>
                  <Badge>endpoint off</Badge>
                </label>
                <label className="row" style={{gap:8, padding:'6px 10px', border:'1px solid #d8d8d8', borderRadius:4, cursor:'pointer'}}>
                  <input type="checkbox" readOnly />
                  <div style={{flex:1}}>
                    <div style={{fontSize:11.5,fontWeight:600}}>Register OData entity sets</div>
                    <div className="muted" style={{fontSize:10.5}}>discoverable via OData service document · check per entity set on the OData service</div>
                  </div>
                </label>
              </div>
            </div>
          </div>

          <div className="col">
            <div className="card" style={{background:'#fffdf3', borderLeft:'3px solid var(--accent-deep)'}}>
              <h3>Catalog registration rules</h3>
              <div className="col" style={{gap:6,fontSize:11}}>
                <div className="row"><Badge>Esri service</Badge><span style={{flex:1,marginLeft:6}}>→</span><Badge kind="info">Esri catalog</Badge><span className="tag" style={{marginLeft:4}}>default on</span></div>
                <div className="row"><Badge>OGC API Features</Badge><span style={{flex:1,marginLeft:6}}>→</span><Badge kind="info">OGC Records</Badge><span className="tag" style={{marginLeft:4}}>default on</span></div>
                <div className="row"><Badge>OData service</Badge><span style={{flex:1,marginLeft:6}}>→</span><Badge kind="info">OData catalog</Badge><span className="tag" style={{marginLeft:4}}>opt-in per entity set</span></div>
                <div className="row"><Badge>resource (any)</Badge><span style={{flex:1,marginLeft:6}}>→</span><Badge kind="info">DCAT · STAC</Badge><span className="tag" style={{marginLeft:4}}>opt-in per resource</span></div>
              </div>
              <div className="muted" style={{fontSize:10.5,marginTop:4}}>Catalog entries are opt-in checkboxes — default on for these pairs. Uncheck if you want the service live but hidden from catalog discovery.</div>
            </div>

            <div className="card">
              <h3>Publish history</h3>
              <div className="col" style={{gap:4,fontSize:11}}>
                {[
                  { v:'v4', t:'2m ago', who:'jamie', tgt:'FS/0, MS/2, OGC API · catalogs: Esri ✓, OGC Records ✓' },,
                  { v:'v3', t:'3d ago', who:'k.tan', tgt:'All slots + WMTS' },
                  { v:'v2', t:'2w ago', who:'jamie', tgt:'OGC API only' },
                  { v:'v1', t:'4w ago', who:'jamie', tgt:'OGC API only' },
                ].map((h,i) => (
                  <div key={i} style={{padding:'3px 0', borderBottom:'1px dashed #eee'}}>
                    <div className="row"><span className="mono"><b>{h.v}</b></span><span className="muted" style={{marginLeft:8}}>{h.who} · {h.t}</span></div>
                    <div className="muted" style={{fontSize:10}}>{h.tgt}</div>
                  </div>
                ))}
              </div>
            </div>
            <Ann red>catalog checkboxes default ON for Esri &amp; OGC API. uncheck only if you specifically need the service live but uncataloged.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResAccess() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="publish" sub="access" />
        <div className="detail">
          <div className="col">
            <div className="card">
              <h3>Who can see this resource</h3>
              <div className="muted" style={{fontSize:11}}>One setting per "audience". Audiences are defined globally and reused everywhere.</div>
              <div className="col" style={{marginTop:6, gap:6}}>
                {[
                  { au:'Public (anonymous)', acc:'view', note:'all fields except owner_name', on:true },
                  { au:'Authenticated users', acc:'view + download', note:'all fields', on:true },
                  { au:'Partners (api key)', acc:'view + download', note:'all fields', on:true },
                  { au:'Internal · GIS team', acc:'edit', note:'full', on:true },
                  { au:'Internal · Auditors', acc:'view', note:'audit fields only', on:false },
                ].map((r,i) => (
                  <div key={i} className="row" style={{border:'1px solid #e4e4e4',borderRadius:4,padding:'6px 10px'}}>
                    <input type="checkbox" readOnly defaultChecked={r.on} />
                    <div style={{flex:1}}>
                      <div style={{fontSize:11.5,fontWeight:600}}>{r.au}</div>
                      <div className="muted" style={{fontSize:10.5}}>{r.note}</div>
                    </div>
                    <span className="tag">{r.acc}</span>
                    <span className="muted">⋯</span>
                  </div>
                ))}
              </div>
            </div>
            <div className="card">
              <h3>Row & field rules</h3>
              <div className="col" style={{gap:6,fontSize:11.5}}>
                <div className="row" style={{border:'1px dashed #c5c5c5',padding:'6px 10px',borderRadius:4}}>
                  <span style={{flex:1}}>Hide <span className="mono">owner_name</span> from <b>Public</b></span>
                  <Badge kind="info">field rule</Badge>
                </div>
                <div className="row" style={{border:'1px dashed #c5c5c5',padding:'6px 10px',borderRadius:4}}>
                  <span style={{flex:1}}>Restrict rows where <span className="mono">use_code = "GOV-SEC"</span> to <b>Internal</b></span>
                  <Badge kind="info">row rule</Badge>
                </div>
                <Btn ghost sm>+ Rule</Btn>
              </div>
            </div>
          </div>
          <div className="col">
            <Callout kind="info">Audiences and rules are defined in Settings → Access. Changes here just bind this resource to existing audiences.</Callout>
            <div className="card">
              <h3>Effective access · sanity check</h3>
              <div className="col" style={{gap:4,fontSize:11}}>
                <div className="row"><span style={{flex:1}}>As <b>anonymous</b></span><Badge kind="ok">view</Badge></div>
                <div className="row"><span style={{flex:1}}>As <b>api-key partner</b></span><Badge kind="ok">view + download</Badge></div>
                <div className="row"><span style={{flex:1}}>As <b>gis-editor</b></span><Badge kind="accent">edit</Badge></div>
                <div className="row"><span style={{flex:1}}>As <b>auditor</b></span><Badge>no access</Badge></div>
              </div>
              <Btn sm>Open simulator…</Btn>
            </div>
            <Ann>simulator lets you paste a token / role and see exactly what they'd get.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResValidation() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead status="warn" />
        <SuperTabs on="operate" sub="validation" />
        <div className="detail">
          <div className="col">
            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
                <h3>Rules</h3>
                <span className="muted" style={{fontSize:11,marginLeft:8}}>12 rules · 2 failing · last run 14m ago</span>
                <div style={{flex:1}}/>
                <Btn sm>Run now</Btn>
                <Btn ghost sm>+ Rule</Btn>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>Rule</th><th>Severity</th><th>Scope</th><th>Last run</th><th>Result</th></tr></thead>
                <tbody>
                  <tr><td><b>CRS set on geom</b></td><td><Badge kind="bad">Block</Badge></td><td>Field · geom</td><td className="muted">14m</td><td><Badge kind="bad">2 fail</Badge></td></tr>
                  <tr><td><b>Geometry valid</b></td><td><Badge kind="bad">Block</Badge></td><td>Resource</td><td className="muted">14m</td><td><Badge kind="ok">pass</Badge></td></tr>
                  <tr><td><b>Non-null parcel_id</b></td><td><Badge kind="bad">Block</Badge></td><td>Field</td><td className="muted">14m</td><td><Badge kind="ok">pass</Badge></td></tr>
                  <tr><td><b>area_m2 &gt; 0</b></td><td><Badge kind="warn">Warn</Badge></td><td>Field</td><td className="muted">14m</td><td><Badge kind="warn">14 warn</Badge></td></tr>
                  <tr><td><b>Schema matches v4</b></td><td><Badge kind="bad">Block</Badge></td><td>Resource</td><td className="muted">14m</td><td><Badge kind="ok">pass</Badge></td></tr>
                  <tr><td><b>Extent within state</b></td><td>Info</td><td>Geometry</td><td className="muted">14m</td><td><Badge kind="ok">pass</Badge></td></tr>
                </tbody>
              </table>
            </div>
            <div className="card">
              <h3>Recent runs</h3>
              <div className="col" style={{gap:4,fontSize:11}}>
                {['14m','2h','1d · all pass','2d · all pass','3d · 4 warn'].map((t,i)=>(
                  <div key={i} className="row" style={{padding:'3px 0',borderBottom:'1px dashed #eee'}}>
                    <span style={{flex:1}}>{t}</span>
                    {i<2 ? <Badge kind="warn">2 fail</Badge> : <Badge kind="ok">pass</Badge>}
                  </div>
                ))}
              </div>
            </div>
          </div>
          <div className="col">
            <Callout kind="bad"><b>Blocked.</b> Validation must pass to publish to OGC Features. Tile service is unaffected.</Callout>
            <div className="card">
              <h3>What to do</h3>
              <ol style={{margin:'0 0 0 16px', padding:0, fontSize:11.5, lineHeight:1.6}}>
                <li>Open the 2 failing rows.</li>
                <li>Set CRS to 4326, or fix at source.</li>
                <li>Run validation. Publish unblocks automatically.</li>
              </ol>
              <div className="row" style={{marginTop:8}}>
                <Btn sm>Open rows</Btn>
                <Btn kind="p" sm>Auto-fix CRS</Btn>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResMetadata() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="define" sub="metadata" />
        <div className="detail" style={{gridTemplateColumns:'1fr 320px'}}>
          <div className="col">
            <div className="card">
              <h3>Identity</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:10}}>
                <Field label="Title"><Inp value="Tax Parcels (FY 2024)" /></Field>
                <Field label="Identifier" hint="urn-style, immutable"><Inp mono value="honua:parcels:2024" /></Field>
                <Field label="Abstract" hint="2–3 sentences. Shown everywhere this resource appears.">
                  <textarea readOnly className="inp" style={{height:60,padding:6,resize:'none'}} defaultValue="Statewide tax parcel boundaries for fiscal year 2024. Sealed from the canonical assessor table. Updates nightly." />
                </Field>
                <Field label="Keywords"><Inp value="parcels, cadastre, assessor, FY2024" /></Field>
                <Field label="Theme"><Sel value="Administrative" /></Field>
                <Field label="Language"><Sel value="en-US" /></Field>
              </div>
            </div>
            <div className="card">
              <h3>Provenance & lineage</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:10}}>
                <Field label="Source organisation"><Inp value="State Assessor's Office" /></Field>
                <Field label="Source contact"><Inp value="data@assessor.ca.gov" /></Field>
                <Field label="Update frequency"><Sel value="Nightly · 02:00 UTC" /></Field>
                <Field label="Temporal coverage"><Inp mono value="2024-01-01 — 2024-12-31" /></Field>
                <Field label="Process steps">
                  <textarea readOnly className="inp" style={{height:60,padding:6,resize:'none'}} defaultValue="1. Pull from prod-postgis  2. Snap to county grid  3. Seal precision to 1mm  4. Publish" />
                </Field>
                <Field label="Quality statement">
                  <textarea readOnly className="inp" style={{height:60,padding:6,resize:'none'}} defaultValue="Positional accuracy ±1m. Attribute completeness ≥ 99.4%." />
                </Field>
              </div>
            </div>
            <div className="card">
              <h3>Licence & contact</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:10}}>
                <Field label="Licence"><Sel value="CC-BY 4.0" /></Field>
                <Field label="Rights statement"><Inp value="© 2024 State Assessor" /></Field>
                <Field label="Point of contact"><Inp value="Jamie Doe · jamie@honua.io" /></Field>
                <Field label="Custodian"><Inp value="GIS team" /></Field>
              </div>
            </div>
          </div>
          <div className="col">
            <div className="card">
              <h3>Maps to</h3>
              <div className="col" style={{gap:4,fontSize:11}}>
                {[
                  ['ISO 19115','core'],
                  ['DCAT 3','core + GeoDCAT'],
                  ['STAC 1.0','geospatial'],
                  ['schema.org/Dataset','public web'],
                ].map(([k,v]) => (
                  <div key={k} className="row" style={{padding:'3px 0',borderBottom:'1px dashed #eee'}}>
                    <span style={{flex:1}}>{k}</span>
                    <Badge kind="ok">mapped</Badge>
                  </div>
                ))}
              </div>
              <Btn sm>Preview as ISO 19115…</Btn>
            </div>
            <Ann>one canonical record. publishers translate to whatever each catalog needs.</Ann>
            <Callout kind="info">Honua records the canonical metadata once; each catalog (STAC, CSW, OGC) sees its own dialect on publish.</Callout>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, {
  ResourcesListA, ResourcesListB,
  ResHead, SuperTabs,
  ResOverview, ResFields, ResPublish, ResAccess, ResValidation, ResMetadata,
});
