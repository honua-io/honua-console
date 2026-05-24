// Services list / Service detail / Services Explorer / Publishing matrix

const SERVICE_FORMATS = [
  'OGC API Features',
  'OGC API Records',
  'WMS','WMTS','WFS',
  'GeoServices FeatureServer','GeoServices MapServer','GeoServices ImageServer',
  'OData','STAC','DCAT','Esri catalog item',
];

function ServicesList() {
  return (
    <div className="scr">
      <TopBar crumbs={['Services & layers']} />
      <Sidebar active="services" />
      <div className="main">
        <PageHead
          title="Services & layers"
          sub={<span>Endpoints that expose your Data Resources. Each service contains one or more layer slots. <span className="muted">Catalog entries (Esri catalog, OGC Records) appear automatically when you publish to Esri or OGC services. DCAT &amp; STAC live in Honua Console.</span></span>}
          actions={<><Btn>Import existing</Btn><Btn kind="p" ico="+">New service</Btn></>}
        />
        <Toolbar
          filters={<>
            <FiltChip on x>kind: any</FiltChip>
            <FiltChip>state: running</FiltChip>
            <FiltChip>anonymous: any</FiltChip>
            <FiltChip>+ filter</FiltChip>
          </>}
          right={<span className="muted" style={{fontSize:11}}>9 endpoints · all healthy</span>}
        />
        <div style={{overflow:'auto',flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Name</th><th>Kind</th><th>Route</th>
              <th className="num">Layers</th><th>Anonymous</th><th>Cache</th><th>State</th><th style={{width:140}}>Actions</th>
            </tr></thead>
            <tbody>
              {[
                { n:'public-works-fs', k:'GeoServices FeatureServer', r:'/public/pw/FeatureServer', res:8, anon:true, c:'30 min', s:'ok' },
                { n:'public-works-ms', k:'GeoServices MapServer', r:'/public/pw/MapServer', res:8, anon:true, c:'1 h', s:'ok' },
                { n:'features-public', k:'OGC API Features', r:'/public/features', res:38, anon:true, c:'30 min', s:'ok' },
                { n:'records-public', k:'OGC API Records', r:'/public/records', res:38, anon:true, c:'1 h', s:'ok' },
                { n:'tiles-public', k:'WMTS', r:'/public/tiles', res:21, anon:true, c:'7 d', s:'ok' },
                { n:'tiles-imagery', k:'WMTS', r:'/public/imagery', res:6, anon:true, c:'30 d', s:'ok' },
                { n:'fs-internal', k:'GeoServices FeatureServer', r:'/internal/fs', res:24, anon:false, c:'5 min', s:'ok' },
                { n:'ms-internal', k:'GeoServices MapServer', r:'/internal/ms', res:12, anon:false, c:'1 h', s:'ok' },
                { n:'odata-bi', k:'OData', r:'/internal/odata', res:8, anon:false, c:'no cache', s:'warn' },
              ].map((r,i) => (
                <tr key={i}>
                  <td><b>{r.n}</b></td>
                  <td>{r.k}</td>
                  <td className="mono">{r.r}</td>
                  <td className="num"><b>{r.res}</b></td>
                  <td>{r.anon ? <Badge kind="accent">yes</Badge> : <Badge>no</Badge>}</td>
                  <td className="mono">{r.c}</td>
                  <td>{r.s === 'ok' ? <Badge kind="ok">Running</Badge> : <Badge kind="warn">Degraded</Badge>}</td>
                  <td>
                    <div className="row" style={{gap:4, fontSize:10.5}}>
                      <a style={{cursor:'pointer'}} title="Open in Honua's map preview">🗺 Map</a>
                      <span style={{color:'#ddd'}}>·</span>
                      <a style={{cursor:'pointer'}} title="Copy full service URL">⧉ Copy URL</a>
                      <span style={{color:'#ddd'}}>·</span>
                      <a style={{cursor:'pointer'}}>⋯</a>
                    </div>
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

function ServiceDetail() {
  // A FeatureServer-style service. Layers are publication slots that point back
  // to canonical Data Resources. Editing meaning routes to the Data Resource.
  return (
    <div className="scr">
      <TopBar crumbs={['Services & layers','public-works-fs']} />
      <Sidebar active="services" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Services & layers <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>public-works-fs</h1>
            <Badge kind="ok" lg>Running</Badge>
            <span className="muted" style={{fontSize:11}}>GeoServices FeatureServer · public · /public/pw/FeatureServer · v1.0</span>
            <div style={{flex:1}}/>
            <Btn ghost>Open REST ↗</Btn>
            <Btn>Stop</Btn>
            <Btn kind="p">+ Add layer</Btn>
          </div>
          <div className="muted" style={{fontSize:11.5, marginTop:6}}>
            8 layers · all bound to canonical Data Resources. This service exposes its layers; <b>it does not own the metadata</b> — meaning lives in the resources.
          </div>

          {/* Service-level URL + quick actions */}
          <div style={{marginTop:10, padding:'8px 10px', background:'#fafafa', border:'1px solid #e4e4e4', borderRadius:5, display:'flex', alignItems:'center', gap:8}}>
            <span className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Service URL</span>
            <code className="mono" style={{flex:1, fontSize:11, color:'#333', overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap'}}>
              https://honua.example.gov/public/pw/FeatureServer
            </code>
            <Btn ghost sm title="Copy service URL — paste into ArcGIS Pro, QGIS, etc.">⧉ Copy URL</Btn>
            <Btn sm title="Open all layers of this service in Honua's map preview">🗺 Map preview</Btn>
            <Btn ghost sm>Open in QGIS ↗</Btn>
            <Btn ghost sm>Open in ArcGIS Pro ↗</Btn>
          </div>
        </div>
        <Tabs items={[
          { k:'overview', t:'Overview' },
          { k:'layers', t:'Layers', ct: 8 },
          { k:'runtime', t:'Runtime settings' },
          { k:'access', t:'Access' },
          { k:'validation', t:'Validation', ct: 1 },
          { k:'jobs', t:'Jobs' },
          { k:'advanced', t:'Advanced' },
        ]} active="layers" />

        <Toolbar
          filters={<>
            <FiltChip on x>state: any</FiltChip>
            <FiltChip>geometry: any</FiltChip>
            <FiltChip>validation: any</FiltChip>
            <input className="inp" style={{width:200,height:22}} placeholder="Filter 8 layers…" readOnly />
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>Drag rows to reorder Layer IDs</span>
            <Btn kind="p" sm>+ Add layer</Btn>
          </>}
        />

        <div style={{overflow:'auto',flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th style={{width:24}}></th>
              <th style={{width:60}}>Layer ID</th>
              <th>Layer name</th>
              <th>Data resource</th>
              <th>Geometry / type</th>
              <th>Status</th>
              <th>Field exposure</th>
              <th>Access</th>
              <th>Validation</th>
              <th>Last published</th>
              <th style={{width:120}}>Actions</th>
            </tr></thead>
            <tbody>
              {[
                { id:0, name:'Parcels',          res:'parcels_2024',   g:'MultiPolygon', s:'ok',    fe:'23 / 24', ac:'Public read',  v:'ok',  p:'2m'  },
                { id:1, name:'Road centerlines', res:'roads_osm',      g:'LineString',   s:'ok',    fe:'7 / 7',   ac:'Public read',  v:'ok',  p:'3d'  },
                { id:2, name:'Hydrants',         res:'hydrants_2024',  g:'Point',        s:'ok',    fe:'12 / 12', ac:'Public read',  v:'ok',  p:'2d'  },
                { id:3, name:'Wetlands',         res:'wetlands_2025',  g:'MultiPolygon', s:'ok',    fe:'18 / 18', ac:'Public read',  v:'warn', p:'14m' },
                { id:4, name:'Fire perimeters',  res:'fire_perimeters',g:'MultiPolygon', s:'warn',  fe:'11 / 12', ac:'Public read',  v:'warn', p:'28m' },
                { id:5, name:'Watersheds',       res:'watersheds_v3',  g:'MultiPolygon', s:'bad',   fe:'21 / 21', ac:'Public read',  v:'bad',  p:'2h'  },
                { id:6, name:'Observation sites',res:'obs_stations',   g:'Point',        s:'ok',    fe:'14 / 14', ac:'Org read',     v:'ok',  p:'1h'  },
                { id:7, name:'Fire observations',res:'fire_observations',g:'Point',      s:'draft', fe:'9 / 9',   ac:'Org read',     v:'na',  p:'—'   },
              ].map((r,i) => (
                <tr key={i} className={i === 0 ? 'sel' : ''}>
                  <td style={{color:'#bbb',cursor:'grab'}}>⋮⋮</td>
                  <td className="mono">{r.id}</td>
                  <td><b>{r.name}</b></td>
                  <td>
                    <span style={{color:'var(--pencil)'}}>◇</span> <a className="mono" style={{color:'var(--pencil)',textDecoration:'underline dotted'}}>{r.res}</a>
                  </td>
                  <td><span className="tag">{r.g}</span></td>
                  <td>
                    {r.s === 'ok'    && <Badge kind="ok">Live</Badge>}
                    {r.s === 'warn'  && <Badge kind="warn">Stale</Badge>}
                    {r.s === 'bad'   && <Badge kind="bad">Blocked</Badge>}
                    {r.s === 'draft' && <Badge kind="draft">Draft</Badge>}
                  </td>
                  <td className="mono" style={{fontSize:10.5}}>{r.fe}</td>
                  <td><span className="tag">{r.ac}</span></td>
                  <td>
                    {r.v === 'ok'   && <Badge kind="ok">pass</Badge>}
                    {r.v === 'warn' && <Badge kind="warn">1 warn</Badge>}
                    {r.v === 'bad'  && <Badge kind="bad">1 fail</Badge>}
                    {r.v === 'na'   && <span className="muted">—</span>}
                  </td>
                  <td className="muted">{r.p}</td>
                  <td>
                    <div className="row" style={{gap:4, fontSize:10.5}}>
                      <a style={{color:'var(--pencil)',cursor:'pointer'}} title="Open the canonical Data Resource">↗ Resource</a>
                      <span style={{color:'#ddd'}}>·</span>
                      <a style={{cursor:'pointer'}} title="Open in Honua's map preview">🗺 Map</a>
                      <span style={{color:'#ddd'}}>·</span>
                      <a style={{cursor:'pointer'}} title="Copy this layer's URL">⧉</a>
                      <span style={{color:'#ddd'}}>·</span>
                      <a style={{cursor:'pointer'}}>⋯</a>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Inline detail strip for selected layer (Parcels) */}
        <div style={{borderTop:'1.5px solid var(--ink)', background:'#fffae0', padding:'10px 18px', display:'grid', gridTemplateColumns:'1.4fr 1fr 1fr auto', gap:14, alignItems:'center'}}>
          <div>
            <div className="row" style={{marginBottom:2}}>
              <b style={{fontSize:12}}>Layer 0 · Parcels</b>
              <span className="tag">/FeatureServer/0</span>
              <Badge kind="ok">live · v4</Badge>
            </div>
            <div className="muted" style={{fontSize:11}}>
              Publication slot → canonical resource <a className="mono" style={{color:'var(--pencil)'}}>◇ parcels_2024</a>.
              Editing meaning (fields, metadata) routes to the resource. Editing presentation in this service (layer name, field aliases, ID) stays here.
            </div>
          </div>
          <div className="col" style={{gap:2,fontSize:11}}>
            <div className="row"><span className="muted" style={{flex:1}}>Layer ID</span><span className="mono">0</span></div>
            <div className="row"><span className="muted" style={{flex:1}}>Layer name</span><span>Parcels</span></div>
            <div className="row"><span className="muted" style={{flex:1}}>Object ID field</span><span className="mono">gid</span></div>
            <div className="row"><span className="muted" style={{flex:1}}>Display field</span><span className="mono">parcel_id</span></div>
          </div>
          <div className="col" style={{gap:2,fontSize:11}}>
            <div className="row"><span className="muted" style={{flex:1}}>Fields exposed</span><span className="mono">23 / 24</span></div>
            <div className="row"><span className="muted" style={{flex:1}}>Field aliases</span><span>5 overridden</span></div>
            <div className="row"><span className="muted" style={{flex:1}}>Access</span><span className="tag">Public read</span></div>
            <div className="row"><span className="muted" style={{flex:1}}>Cache</span><span>30 min</span></div>
          </div>
          <div className="col" style={{gap:4}}>
            <Btn sm>↗ Open resource</Btn>
            <Btn sm>🗺 Map preview</Btn>
            <Btn sm>⧉ Copy URL</Btn>
            <Btn sm>Edit publication</Btn>
            <Btn sm>Validate</Btn>
            <Btn ghost sm style={{color:'var(--bad)', borderColor:'#e7a59c'}}>Unpublish layer</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- Publishing matrix A: grid ---------- */
function PublishMatrixA() {
  const resources = [
    'parcels_2024','wetlands_2025','fire_perimeters','fire_observations',
    'obs_stations','watersheds_v3','land_cover_2024','sentinel_2_tiles',
    'roads_osm','census_blocks','air_quality_obs','noaa_wms_layers',
  ];
  const cols = ['OGC API Features','OGC API Records','WMS','WMTS','WFS','FeatureServer','MapServer','ImageServer','OData','STAC','DCAT','Esri catalog'];

  // state[res][format]: 'pub' | 'drft' | 'err' | null (not applicable)
  function st(r, c) {
    const map = {
      'parcels_2024': { 'OGC API Features':'pub','OGC API Records':'pub','WMS':'pub','WMTS':'pub','FeatureServer':'pub','STAC':'pub','DCAT':'pub','MapServer':'pub' },
      'wetlands_2025': { 'OGC API Features':'pub','OGC API Records':'pub','WMS':'pub','WMTS':'pub','FeatureServer':'pub','STAC':'pub','DCAT':'pub' },
      'fire_perimeters': { 'OGC API Features':'pub','WMS':'pub','WMTS':'err','FeatureServer':'pub','STAC':'pub','DCAT':'pub' },
      'fire_observations': { 'OGC API Features':'drft','FeatureServer':'drft','STAC':'drft' },
      'obs_stations': { 'OGC API Features':'pub','WMS':'pub','FeatureServer':'pub' },
      'watersheds_v3': { 'OGC API Features':'err','FeatureServer':'drft','STAC':'drft','DCAT':'drft' },
      'land_cover_2024': { 'WMS':'pub','WMTS':'pub','ImageServer':'pub','STAC':'pub' },
      'sentinel_2_tiles': { 'WMTS':'pub','ImageServer':'pub','STAC':'pub' },
      'roads_osm': { 'OGC API Features':'pub','WMS':'pub','WMTS':'pub','FeatureServer':'pub','MapServer':'pub','STAC':'pub' },
      'census_blocks': { 'OGC API Features':'pub','FeatureServer':'pub','OData':'pub','DCAT':'pub' },
      'air_quality_obs': { 'OGC API Features':'pub','FeatureServer':'pub' },
      'noaa_wms_layers': { 'WMS':'pub','WMTS':'pub' },
    };
    return map[r]?.[c] || null;
  }
  function cell(s) {
    if (s === 'pub') return <span className="cell pub">●</span>;
    if (s === 'drft') return <span className="cell drft">○ draft</span>;
    if (s === 'err') return <span className="cell err">✕</span>;
    return <span className="cell off">·</span>;
  }
  return (
    <div className="scr">
      <TopBar crumbs={['Publishing']} />
      <Sidebar active="publishing" />
      <div className="main">
        <PageHead
          title="Publishing"
          sub="One row per resource. One column per service / catalog format. Each cell is a publication."
          actions={<><Btn>View B · per resource</Btn><Btn kind="p">Bulk publish…</Btn></>}
        />
        <Toolbar
          filters={<>
            <FiltChip on x>scope: my resources</FiltChip>
            <FiltChip>type: any</FiltChip>
            <FiltChip>has draft</FiltChip>
            <FiltChip>blocked</FiltChip>
          </>}
          right={<>
            <span className="row" style={{gap:10,fontSize:10.5}}>
              <span className="row"><span className="cell pub" style={{marginRight:4}}>●</span>Live</span>
              <span className="row"><span className="cell drft" style={{marginRight:4}}>○</span>Draft</span>
              <span className="row"><span className="cell err" style={{marginRight:4}}>✕</span>Blocked</span>
              <span className="row"><span className="cell off" style={{marginRight:4}}>·</span>n/a</span>
            </span>
          </>}
        />
        <div style={{overflow:'auto',flex:1,padding:'8px 18px 18px'}}>
          <table className="matrix">
            <thead>
              <tr>
                <th className="row" style={{minWidth:200}}>Resource</th>
                {cols.map(c => (
                  <th key={c} style={{minWidth:78,fontSize:10}}>
                    <div style={{transform:'rotate(-12deg)', transformOrigin:'left bottom', whiteSpace:'nowrap',display:'inline-block'}}>{c}</div>
                  </th>
                ))}
                <th style={{width:60}}>Total</th>
              </tr>
            </thead>
            <tbody>
              {resources.map(r => {
                const n = cols.filter(c => st(r,c) === 'pub').length;
                const issues = cols.filter(c => st(r,c) === 'err' || st(r,c) === 'drft').length;
                return (
                  <tr key={r}>
                    <td className="row">
                      <span style={{color:'var(--pencil)'}}>◇</span> <b>{r}</b>
                      {issues > 0 && <span className="muted" style={{marginLeft:6,fontSize:10}}>{issues} pending</span>}
                    </td>
                    {cols.map(c => (
                      <td key={c} onClick={()=>{}}>{cell(st(r,c))}</td>
                    ))}
                    <td className="mono"><b>{n}</b></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <div className="row" style={{marginTop:14}}>
            <Ann>cells are clickable. selecting opens the projection preview drawer →</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- Publishing matrix B: per resource panel + projection preview drawer ---------- */
function PublishMatrixB() {
  return (
    <div className="scr" style={{position:'relative'}}>
      <TopBar crumbs={['Publishing','parcels_2024']} />
      <Sidebar active="publishing" />
      <div className="main">
        <PageHead
          title="Publish · parcels_2024"
          sub="Per-resource panel · use this when you want to compare projections side-by-side"
          actions={<><Btn>Back to matrix</Btn><Btn kind="p">Publish selected</Btn></>}
        />
        <div style={{padding:'14px 18px', overflow:'auto', flex:1}}>
          <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
            {[
              { f:'OGC API Features', svc:'features-public', path:'/collections/parcels_2024', s:'live', ver:'v4', fields:'23 / 24', note:'fully compatible' },
              { f:'OGC API Records',  svc:'records-public', path:'/records/parcels_2024',     s:'live', ver:'v4', fields:'metadata only', note:'' },
              { f:'WMS',              svc:'tiles-public',   path:'/wms?LAYERS=parcels_2024',  s:'live', ver:'v4', fields:'9 fields in GetFeatureInfo', note:'' },
              { f:'WMTS',             svc:'tiles-public',   path:'/wmts/parcels_2024',        s:'live', ver:'v4', fields:'tile pyramid 4 levels', note:'' },
              { f:'WFS',              svc:'fs-internal',    path:'/wfs?TYPENAME=parcels_2024',s:'draft', ver:'—', fields:'inherits canonical', note:'requires authentication' },
              { f:'FeatureServer',    svc:'fs-internal',    path:'/internal/fs/0',            s:'live', ver:'v4', fields:'23 / 24', note:'authenticated' },
              { f:'MapServer',        svc:'ms-internal',    path:'/internal/ms/0',            s:'live', ver:'v4', fields:'12 fields in identify', note:'' },
              { f:'STAC',             svc:'stac-public',    path:'/collections/parcels_2024', s:'live', ver:'v4', fields:'collection level', note:'' },
              { f:'DCAT',             svc:'dcat-eu',        path:'/dcat/parcels_2024',        s:'live', ver:'v4', fields:'metadata only', note:'' },
              { f:'ImageServer',      svc:'—',              path:'—',                          s:'na', ver:'—', fields:'—', note:'feature data, not raster' },
              { f:'OData',            svc:'odata-bi',       path:'/odata/Parcels',            s:'live', ver:'v4', fields:'18 / 24', note:'no geometry' },
              { f:'Esri catalog',     svc:'esri-catalog',   path:'catalog / item / abc123',   s:'warn', ver:'v3', fields:'metadata only', note:'thumbnail missing' },
            ].map((p,i) => {
              const selected = i === 2; // show drawer for WMS row
              return (
                <div key={i} className="card" style={{padding:0, opacity: p.s === 'na' ? 0.55 : 1, borderColor: selected ? 'var(--ink)' : '#e4e4e4'}}>
                  <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
                    <h3 style={{flex:1}}>{p.f}</h3>
                    {p.s === 'live' && <Badge kind="ok">Live · {p.ver}</Badge>}
                    {p.s === 'draft' && <Badge kind="draft">Draft</Badge>}
                    {p.s === 'warn' && <Badge kind="warn">Needs review</Badge>}
                    {p.s === 'na' && <Badge>n/a</Badge>}
                  </div>
                  <div style={{padding:'8px 12px', fontSize:11}}>
                    <div className="row"><span className="muted" style={{flex:1}}>Service</span><span className="mono">{p.svc}</span></div>
                    <div className="row"><span className="muted" style={{flex:1}}>Path</span><span className="mono" style={{fontSize:10}}>{p.path}</span></div>
                    <div className="row"><span className="muted" style={{flex:1}}>Fields</span><span>{p.fields}</span></div>
                    {p.note && <div className="muted" style={{marginTop:4,fontSize:10.5}}>{p.note}</div>}
                  </div>
                  <div style={{padding:'6px 12px',display:'flex',gap:6, borderTop:'1px dashed #eee', background:'#fafafa'}}>
                    <Btn sm>Preview</Btn>
                    <Btn sm>Configure</Btn>
                    <div style={{flex:1}}/>
                    {p.s !== 'na' && <Btn kind="p" sm>{p.s === 'draft' ? 'Publish' : 'Republish'}</Btn>}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {/* Projection preview drawer */}
      <div className="drawer">
        <h2>
          Projection preview
          <span className="tag" style={{marginLeft:4}}>WMS</span>
          <div style={{flex:1}}/>
          <span className="muted" style={{fontSize:10}}>compare ↔</span>
          <span style={{cursor:'pointer'}}>×</span>
        </h2>
        <div style={{padding:'10px 14px', flex:1, overflow:'auto'}}>
          <div className="muted" style={{fontSize:11, marginBottom:10}}>
            How <b>parcels_2024</b> will appear when published as WMS through <span className="mono">tiles-public</span>.
          </div>
          <Tabs sub items={[
            { k:'preview', t:'Preview' },
            { k:'caps', t:'GetCapabilities' },
            { k:'meta', t:'Metadata projection' },
            { k:'fields', t:'GetFeatureInfo fields' },
          ]} active="caps" />
          <div style={{padding:'10px 0', fontSize:10.5}}>
            <pre className="mono" style={{margin:0, lineHeight:1.5, background:'#fafafa', padding:10, borderRadius:4, border:'1px solid #eee', whiteSpace:'pre-wrap'}}>
{`<WMS_Capabilities version="1.3.0">
  <Service>
    <Title>features-public</Title>
    <Abstract>Honua public WMS</Abstract>
  </Service>
  <Capability>
    <Layer>
      <Title>parcels_2024</Title>
      <CRS>EPSG:4326</CRS>
      <CRS>EPSG:3857</CRS>
      <BoundingBox CRS="EPSG:4326"
        minx="-124.4" miny="32.5" maxx="-114.1" maxy="42.0"/>
      <Style><Name>default</Name></Style>
      <MinScaleDenominator>500</MinScaleDenominator>
      <MaxScaleDenominator>15000000</MaxScaleDenominator>
    </Layer>
  </Capability>
</WMS_Capabilities>`}
            </pre>
          </div>
          <Callout kind="info">Source of truth is the resource's canonical metadata. This XML is generated on publish; you can override per-target in <b>Configure</b>.</Callout>
          <div style={{marginTop:10}}>
            <div className="row" style={{fontSize:11, marginBottom:6}}><b>Fields shown in GetFeatureInfo</b><span style={{flex:1}}/><Btn sm>Edit</Btn></div>
            <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
              <thead><tr><th>Field</th><th>Alias</th><th>Show?</th></tr></thead>
              <tbody>
                <tr><td className="mono">parcel_id</td><td>Parcel ID</td><td>✓</td></tr>
                <tr><td className="mono">area_m2</td><td>Area (m²)</td><td>✓</td></tr>
                <tr><td className="mono">use_code</td><td>Use</td><td>✓</td></tr>
                <tr><td className="mono">owner_name</td><td>Owner</td><td className="muted">redacted</td></tr>
              </tbody>
            </table>
          </div>
        </div>
        <div style={{padding:'8px 14px', borderTop:'1px solid #e4e4e4', display:'flex', gap:6}}>
          <Btn ghost sm>Save as draft</Btn>
          <div style={{flex:1}}/>
          <Btn sm>Configure…</Btn>
          <Btn kind="p" sm>Publish</Btn>
        </div>
      </div>
    </div>
  );
}

function ServicesExplorer() {
  // Tree-style explorer of Honua's own services. Folders → Services → Layers.
  // Right pane: detail of the selected service (or layer, when one's picked).
  const T = ({ depth = 0, icon, name, meta, on, open, tone, kind }) => (
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
      {kind && <span className="tag" style={{ marginRight: 6, fontSize: 9 }}>{kind}</span>}
      {meta && <span className="muted mono" style={{ fontSize: 9.5 }}>{meta}</span>}
    </div>
  );

  return (
    <div className="scr" style={{position:'relative'}}>
      <TopBar crumbs={['Services & layers','Explorer']} />
      <Sidebar active="services" />
      <div className="main">
        <PageHead
          title="Services & layers · explorer"
          sub="Browse the whole publish surface: folders → services → layers. Right-click any node for context actions. Layers are publication slots back to canonical Data Resources."
          actions={<><Btn>Switch to list</Btn><Btn kind="p">+ New service</Btn></>}
        />
        <Toolbar
          filters={<>
            <FiltChip on x>kind: any</FiltChip>
            <FiltChip>state: running</FiltChip>
            <FiltChip>has layers</FiltChip>
            <input className="inp" style={{ width: 220, height: 22 }} placeholder="Filter services & layers…" readOnly />
          </>}
          right={<span className="muted" style={{ fontSize: 11 }}>9 services · 4 folders · 84 layer slots</span>}
        />

        <div style={{ display: 'grid', gridTemplateColumns: '420px 1fr', flex: 1, overflow: 'hidden' }}>
          {/* TREE */}
          <div style={{ borderRight: '1px solid #e4e4e4', overflow: 'auto', background: '#fafafa' }}>
            <div style={{ padding: '6px 10px', fontSize: 10, color: '#888', textTransform: 'uppercase', letterSpacing: '0.06em', background: '#f1f1f1', borderBottom: '1px solid #e0e0e0' }}>
              honua / public
            </div>
            <T depth={0} icon="▤" name="(root)" meta="9 services" open={true} />

            <T depth={1} icon="🗀" name="public" meta="5 svc · 56 lyr" open={true} />

              <T depth={2} icon="◈" name="public-works-fs" kind="FeatureServer" meta="8 lyr · running" open={true} on={false} />
                <T depth={3} icon="◇" name="0 · Parcels" meta="MultiPolygon · 1.28M · live" tone="var(--pencil)" on={true} />
                <T depth={3} icon="◇" name="1 · Road centerlines" meta="LineString · 482k · live" tone="var(--pencil)" />
                <T depth={3} icon="◇" name="2 · Hydrants" meta="Point · 14k · live" tone="var(--pencil)" />
                <T depth={3} icon="◇" name="3 · Wetlands" meta="MultiPolygon · 82k · live" tone="var(--pencil)" />
                <T depth={3} icon="◇" name="4 · Fire perimeters" meta="MultiPolygon · 14k · stale" tone="var(--pencil)" />
                <T depth={3} icon="◇" name="5 · Watersheds" meta="MultiPolygon · 18k · blocked" tone="var(--bad)" />
                <T depth={3} icon="◇" name="6 · Observation sites" meta="Point · 4.2k · live" tone="var(--pencil)" />
                <T depth={3} icon="◇" name="7 · Fire observations" meta="Point · draft" tone="var(--pencil)" />

              <T depth={2} icon="◈" name="public-works-ms" kind="MapServer" meta="8 lyr · running" open={false} />
              <T depth={2} icon="◈" name="features-public" kind="OGC API Features" meta="38 lyr · running" open={false} />
              <T depth={2} icon="◈" name="tiles-public" kind="WMTS" meta="21 lyr · running" open={false} />
              <T depth={2} icon="◈" name="stac-public" kind="STAC" meta="42 collections · running" open={false} />

            <T depth={1} icon="🗀" name="catalogs" meta="2 svc · 92 records" open={true} />
              <T depth={2} icon="◈" name="dcat-eu" kind="DCAT" meta="54 datasets · running" open={false} />
              <T depth={2} icon="◈" name="records-public" kind="OGC Records" meta="38 records · running" open={false} />

            <T depth={1} icon="🗀" name="internal" meta="3 svc · 28 lyr · auth required" open={true} />
              <T depth={2} icon="◈" name="fs-internal" kind="FeatureServer" meta="24 lyr · running" open={false} />
              <T depth={2} icon="◈" name="ms-internal" kind="MapServer" meta="12 lyr · running" open={false} />
              <T depth={2} icon="◈" name="odata-bi" kind="OData" meta="8 entity sets · degraded" open={false} tone="var(--warn)" />

            <T depth={1} icon="🗀" name="imagery" meta="1 svc · raster" open={false} />
              <T depth={2} icon="◈" name="tiles-imagery" kind="WMTS" meta="6 lyr · running" open={false} />

            <div style={{ padding: '10px 12px', borderTop: '1px dashed #d8d8d8', fontSize: 10.5, color: '#888' }}>
              Tip · right-click a folder → Add service · right-click a service → Add layer · right-click a layer → Open Data Resource, Copy URL, Unpublish.
            </div>
          </div>

          {/* CONTEXT MENU — anchored to the "public" folder node */}
          <div style={{
            position:'absolute',
            top: 184,
            left: 254,
            background:'#fff',
            border:'1.2px solid var(--ink)',
            borderRadius:6,
            boxShadow:'0 8px 24px rgba(0,0,0,.18)',
            padding:'4px 0',
            minWidth:220,
            fontSize:11.5,
            zIndex: 4,
          }}>
            <div style={{padding:'4px 12px', borderBottom:'1px solid #eee', display:'flex', alignItems:'center', gap:6, background:'#fafafa'}}>
              <span style={{color:'#666'}}>🗀</span>
              <b>public</b>
              <span className="muted mono" style={{fontSize:10}}>· folder</span>
            </div>

            {[
              { i:'＋', t:'Add service…',          k:'⌘N',  hot:true },
              { i:'＋', t:'Add folder…',           k:'' },
              null,
              { i:'✎',  t:'Rename folder',         k:'F2' },
              { i:'↷',  t:'Move folder…',          k:'' },
              null,
              { i:'⧉',  t:'Copy folder URL',       k:'⌘C' },
              { i:'⚿',  t:'Edit folder access…',   k:'' },
              null,
              { i:'⊟',  t:'Collapse all',          k:'' },
              null,
              { i:'✕',  t:'Delete folder',         k:'⌫', danger:true, disabled:true },
            ].map((it, i) => it === null ? (
              <div key={i} style={{height:1, background:'#eee', margin:'4px 0'}} />
            ) : (
              <div key={i} style={{
                padding:'4px 12px',
                display:'flex',
                alignItems:'center',
                gap:8,
                cursor: it.disabled ? 'default' : 'pointer',
                opacity: it.disabled ? 0.4 : 1,
                color: it.danger ? 'var(--bad)' : 'var(--ink)',
                background: it.hot ? '#fffae0' : 'transparent',
                fontWeight: it.hot ? 600 : 400,
              }}>
                <span style={{width:14, textAlign:'center', color: it.danger ? 'var(--bad)' : '#666'}}>{it.i}</span>
                <span style={{flex:1}}>{it.t}</span>
                {it.k && <span className="mono" style={{fontSize:9.5, color:'#aaa'}}>{it.k}</span>}
              </div>
            ))}

            {/* hint that this is the open menu */}
            <div style={{padding:'4px 12px 6px', borderTop:'1px solid #eee', background:'#fafafa', fontSize:9.5, color:'#888', display:'flex', alignItems:'center', gap:6}}>
              <span style={{fontFamily:'var(--mono)', padding:'1px 4px', background:'#fff', border:'1px solid #ddd', borderRadius:2}}>right-click</span>
              <span>or hover &amp; click ⋯</span>
            </div>
          </div>

          {/* SECOND CONTEXT MENU — anchored to public-works-fs service node — shown as a peek/preview */}
          <div style={{
            position:'absolute',
            top: 260,
            left: 110,
            background:'#fff',
            border:'1px solid var(--line-soft)',
            borderRadius:6,
            boxShadow:'0 8px 24px rgba(0,0,0,.10)',
            padding:'4px 0',
            minWidth:200,
            fontSize:11,
            zIndex: 3,
            opacity: 0.85,
          }}>
            <div style={{padding:'4px 12px', borderBottom:'1px solid #eee', display:'flex', alignItems:'center', gap:6, background:'#fafafa', fontSize:10.5}}>
              <span style={{color:'#666'}}>◈</span>
              <b>public-works-fs</b>
              <span className="muted mono" style={{fontSize:9.5}}>· FeatureServer</span>
            </div>
            {[
              { i:'＋', t:'Add layer…', hot:true },
              { i:'🗺', t:'Map preview · whole service' },
              { i:'⧉',  t:'Copy service URL' },
              null,
              { i:'✎',  t:'Edit settings' },
              { i:'↷',  t:'Move to folder…' },
              { i:'⏸',  t:'Stop service' },
              null,
              { i:'✕',  t:'Delete service', danger:true },
            ].map((it,i) => it === null ? (
              <div key={i} style={{height:1, background:'#eee', margin:'3px 0'}} />
            ) : (
              <div key={i} style={{
                padding:'3px 12px',
                display:'flex', alignItems:'center', gap:8,
                color: it.danger ? 'var(--bad)' : 'var(--ink)',
                background: it.hot ? '#fffae0' : 'transparent',
                fontWeight: it.hot ? 600 : 400,
              }}>
                <span style={{width:14, textAlign:'center', color: it.danger ? 'var(--bad)' : '#666'}}>{it.i}</span>
                <span style={{flex:1}}>{it.t}</span>
              </div>
            ))}
          </div>

          {/* DETAIL — selected: public-works-fs / layer 0 · Parcels */}
          <div style={{ overflow: 'auto' }}>
            <div style={{ padding: '12px 18px', borderBottom: '1px solid #e4e4e4' }}>
              <div className="muted" style={{ fontSize: 10.5 }}>public / public-works-fs / layer 0</div>
              <div className="row" style={{ marginTop: 2 }}>
                <h2 style={{ margin: 0, font: '600 16px var(--ui)' }}>Parcels</h2>
                <span className="tag">FeatureServer · layer 0</span>
                <Badge kind="ok">Live · v4</Badge>
                <div style={{ flex: 1 }} />
                <Btn ghost sm>Open REST ↗</Btn>
                <Btn sm>Validate</Btn>
                <Btn kind="p" sm>↗ Open Data Resource</Btn>
              </div>
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                Publication slot. Canonical home: <a className="mono" style={{ color: 'var(--pencil)' }}>◇ parcels_2024</a>.
                Editing meaning (fields, metadata, semantics) routes there. Editing presentation here (layer name, ID, field aliases, cache) stays in this service.
              </div>

              {/* URL + actions row */}
              <div style={{marginTop:10, padding:'8px 10px', background:'#fafafa', border:'1px solid #e4e4e4', borderRadius:5, display:'flex', alignItems:'center', gap:8}}>
                <span className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Layer URL</span>
                <code className="mono" style={{flex:1, fontSize:11, color:'#333', overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap'}}>
                  https://honua.example.gov/public/pw/FeatureServer/0
                </code>
                <Btn ghost sm title="Copy full URL to clipboard">⧉ Copy URL</Btn>
                <Btn sm title="Open this layer in Honua's map preview">🗺 Map preview</Btn>
                <Btn ghost sm>Open in QGIS ↗</Btn>
                <Btn ghost sm>Open in ArcGIS Pro ↗</Btn>
              </div>
            </div>

            <div style={{ padding: '12px 18px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
              <div className="card" style={{ gap: 6 }}>
                <h3>Slot identity</h3>
                <dl className="kv">
                  <dt>Service</dt><dd className="mono">public-works-fs</dd>
                  <dt>Layer ID</dt><dd className="mono">0</dd>
                  <dt>Layer name</dt><dd>Parcels</dd>
                  <dt>Service URL</dt><dd className="mono" style={{ fontSize: 10 }}>/public/pw/FeatureServer/0</dd>
                  <dt>Object ID field</dt><dd className="mono">gid</dd>
                  <dt>Display field</dt><dd className="mono">parcel_id</dd>
                </dl>
                <Btn sm>Edit publication settings</Btn>
              </div>
              <div className="card" style={{ gap: 6 }}>
                <h3>From the canonical resource</h3>
                <dl className="kv">
                  <dt>Data Resource</dt><dd><span style={{color:'var(--pencil)'}}>◇</span> parcels_2024</dd>
                  <dt>Geometry</dt><dd>MultiPolygon · 4326</dd>
                  <dt>Features</dt><dd className="mono">1,284,021</dd>
                  <dt>Fields</dt><dd>24 canonical · 23 exposed here</dd>
                  <dt>Access</dt><dd><span className="tag">Public read</span></dd>
                  <dt>Validation</dt><dd><Badge kind="ok">pass</Badge></dd>
                </dl>
              </div>
            </div>

            <div style={{ padding: '0 18px 12px' }}>
              <div className="card" style={{ padding: 0 }}>
                <div style={{ padding: '8px 12px', borderBottom: '1px solid #e4e4e4', display:'flex', alignItems:'center' }}>
                  <h3>Field exposure in this slot</h3>
                  <span className="muted" style={{ fontSize: 11, marginLeft: 8 }}>presentation only — semantic roles live on the resource</span>
                  <div style={{ flex: 1 }} />
                  <Btn ghost sm>Reset to canonical</Btn>
                  <Btn sm>Edit aliases</Btn>
                </div>
                <table className="tbl tbl--cmpt">
                  <thead><tr>
                    <th>Canonical field</th><th>Role</th><th>Slot alias</th><th>Exposed?</th><th>Override?</th>
                  </tr></thead>
                  <tbody>
                    <tr><td className="mono"><b>gid</b></td><td><Badge kind="accent">Primary ID</Badge></td><td>OBJECTID</td><td>✓</td><td className="muted">—</td></tr>
                    <tr><td className="mono">parcel_id</td><td>Display</td><td>Parcel ID</td><td>✓</td><td><Badge>alias</Badge></td></tr>
                    <tr><td className="mono">area_m2</td><td>—</td><td>Area (m²)</td><td>✓</td><td><Badge>alias</Badge></td></tr>
                    <tr><td className="mono">use_code</td><td>Category</td><td>Use</td><td>✓</td><td><Badge>alias</Badge></td></tr>
                    <tr><td className="mono">owner_name</td><td><Badge kind="warn">Sensitive</Badge></td><td className="muted">—</td><td className="muted">hidden</td><td><Badge>hide</Badge></td></tr>
                    <tr><td className="mono">last_assessment</td><td>Temporal</td><td>Last assessed</td><td>✓</td><td><Badge>alias</Badge></td></tr>
                    <tr><td className="mono" style={{ color: '#888' }}>+ 18 more</td><td colSpan="4" className="muted">all inherit canonical exposure</td></tr>
                  </tbody>
                </table>
              </div>
            </div>

            <div style={{ padding: '0 18px 14px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
              <div className="card">
                <h3>Runtime</h3>
                <dl className="kv">
                  <dt>Anonymous</dt><dd><Badge kind="accent">yes</Badge></dd>
                  <dt>Max record count</dt><dd className="mono">5,000</dd>
                  <dt>Output formats</dt><dd>JSON, GeoJSON, PBF</dd>
                  <dt>Cache TTL</dt><dd>30 min</dd>
                </dl>
              </div>
              <div className="card">
                <h3>Last publish</h3>
                <dl className="kv">
                  <dt>Version</dt><dd className="mono">v4</dd>
                  <dt>By</dt><dd>jamie</dd>
                  <dt>When</dt><dd>2m ago</dd>
                  <dt>Diff</dt><dd>+1 field alias, cache TTL 60 → 30 min</dd>
                </dl>
                <Btn sm>View history</Btn>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ServiceRuntimeSettings() {
  // Service detail · Runtime settings tab. FeatureServer example.
  const Group = ({ title, sub, children }) => (
    <div className="card" style={{ padding: 0 }}>
      <div style={{ padding: '8px 12px', borderBottom: '1px solid #e4e4e4', display: 'flex', alignItems: 'center', gap: 8, background: '#fafafa' }}>
        <h3 style={{ margin: 0 }}>{title}</h3>
        {sub && <span className="muted" style={{ fontSize: 10.5 }}>{sub}</span>}
      </div>
      <div style={{ padding: '10px 12px' }}>
        {children}
      </div>
    </div>
  );

  const Row = ({ label, hint, children }) => (
    <div style={{ display: 'grid', gridTemplateColumns: '180px 1fr', alignItems: 'start', columnGap: 12, padding: '6px 0', borderBottom: '1px dashed #eee' }}>
      <div style={{ paddingTop: 4 }}>
        <div style={{ fontSize: 11.5, fontWeight: 500 }}>{label}</div>
        {hint && <div className="muted" style={{ fontSize: 10.5, marginTop: 1 }}>{hint}</div>}
      </div>
      <div>{children}</div>
    </div>
  );

  const Check = ({ on, lab, dis }) => (
    <label className="row" style={{ gap: 6, fontSize: 11, opacity: dis ? 0.5 : 1, cursor: dis ? 'default' : 'pointer' }}>
      <input type="checkbox" readOnly defaultChecked={on} disabled={dis} />
      <span>{lab}</span>
    </label>
  );

  return (
    <div className="scr">
      <TopBar crumbs={['Services & layers', 'public-works-fs']} />
      <Sidebar active="services" />
      <div className="main">
        <div style={{ padding: '12px 18px 0' }}>
          <div className="muted" style={{ fontSize: 11 }}>Services & layers <span style={{ color: '#bbb' }}>/</span></div>
          <div className="row">
            <h1 style={{ margin: 0, font: '600 18px var(--ui)' }}>public-works-fs</h1>
            <Badge kind="ok" lg>Running</Badge>
            <span className="muted" style={{ fontSize: 11 }}>GeoServices FeatureServer · public · /public/pw/FeatureServer · v1.0</span>
            <div style={{ flex: 1 }} />
            <Btn ghost>Discard</Btn>
            <Btn kind="p">Save · restart service</Btn>
          </div>
        </div>

        <Tabs items={[
          { k: 'overview', t: 'Overview' },
          { k: 'layers', t: 'Layers', ct: 8 },
          { k: 'runtime', t: 'Runtime settings' },
          { k: 'access', t: 'Access' },
          { k: 'validation', t: 'Validation', ct: 1 },
          { k: 'jobs', t: 'Jobs' },
          { k: 'advanced', t: 'Advanced' },
        ]} active="runtime" />

        {/* Two-column body: settings (left) + side reference (right) */}
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 320px', gap: 14, padding: '14px 18px', overflow: 'auto', flex: 1 }}>
          <div className="col" style={{ gap: 12 }}>

            {/* IDENTITY */}
            <Group title="Identity">
              <Row label="Service name" hint="lowercase, dashes ok"><Inp mono value="public-works-fs" /></Row>
              <Row label="Display title" hint="shown in REST root + catalog"><Inp value="Public Works (parcels, roads, hydrants)" /></Row>
              <Row label="Description">
                <textarea readOnly className="inp" style={{ height: 56, padding: 6, resize: 'none' }} defaultValue="Public-facing FeatureServer with parcels, roads, hydrants, wetlands and observation sites. Refreshed nightly from prod-postgis." />
              </Row>
              <Row label="Tags"><Inp value="public, parcels, infrastructure" /></Row>
              <Row label="Folder" hint="derives route prefix"><Sel value="public" /></Row>
              <Row label="Service kind" hint="not editable — make a new service to change kind"><Sel value="GeoServices FeatureServer" /></Row>
              <Row label="Route" hint="derived: /folder/name/kind">
                <div style={{ height: 26, border: '1px dashed #c0c0c0', borderRadius: 4, background: '#fafafa', padding: '0 8px', display: 'flex', alignItems: 'center', font: '11px var(--mono)', color: '#666' }}>
                  /public/pw/FeatureServer
                  <span style={{ flex: 1 }} />
                  <span style={{ color: '#bbb', fontSize: 9.5 }}>auto</span>
                </div>
              </Row>
            </Group>

            {/* SPATIAL REFERENCE */}
            <Group title="Spatial reference & extent">
              <Row label="Default CRS" hint="what the service reports as native"><Sel value="EPSG:4326 · WGS 84" /></Row>
              <Row label="Allow CRS transformations" hint="serve in other CRSs at query time">
                <div className="col" style={{ gap: 3, fontSize: 11 }}>
                  <Check on lab="EPSG:3857 (Web Mercator)" />
                  <Check on lab="EPSG:4269 (NAD83)" />
                  <Check on lab="EPSG:2227 (CA State Plane)" />
                  <Check lab="any (consumer-specified)" />
                </div>
              </Row>
              <Row label="Service extent" hint="bounding box that envelops all layers">
                <div className="row" style={{ gap: 6, fontSize: 11 }}>
                  <span className="mono" style={{ background: '#fafafa', border: '1px solid #e4e4e4', padding: '2px 6px', borderRadius: 3 }}>-124.4, 32.5</span>
                  <span className="muted">to</span>
                  <span className="mono" style={{ background: '#fafafa', border: '1px solid #e4e4e4', padding: '2px 6px', borderRadius: 3 }}>-114.1, 42.0</span>
                  <Btn ghost sm>Recompute from layers</Btn>
                </div>
              </Row>
              <Row label="Z-aware" hint="layers may carry elevation"><Check lab="enabled" /></Row>
              <Row label="M-aware" hint="layers may carry measure values"><Check lab="enabled" /></Row>
            </Group>

            {/* LIMITS */}
            <Group title="Query limits">
              <Row label="Default max record count" hint="per request. layers can override.">
                <div className="row" style={{ gap: 6 }}>
                  <Inp mono value="5,000" />
                  <span className="muted" style={{ fontSize: 10.5 }}>hard cap: 50,000</span>
                </div>
              </Row>
              <Row label="Max page size on extract"><Inp mono value="50,000" /></Row>
              <Row label="Default visible scale range">
                <div className="row" style={{ gap: 6 }}>
                  <Inp mono value="1 : 500" /><span className="muted">to</span><Inp mono value="1 : 15,000,000" />
                </div>
              </Row>
              <Row label="Standardized queries only" hint="block raw SQL functions in where clauses"><Check on lab="enforced" /></Row>
              <Row label="Allow non-geographic queries" hint="GetFeatureInfo without geometry"><Check on lab="allowed" /></Row>
            </Group>

            {/* CAPABILITIES */}
            <Group title="Capabilities" sub="what consumers can do against this service">
              <Row label="Query operations">
                <div className="col" style={{ gap: 3 }}>
                  <Check on lab="Query (read features by where + geometry)" />
                  <Check on lab="Extract (bulk download)" />
                  <Check on lab="GetFeatureInfo · HTML" />
                  <Check on lab="Statistics (sum, count, mean…)" />
                </div>
              </Row>
              <Row label="Editing operations" hint="this service is read-only — turn editing on per-layer">
                <div className="col" style={{ gap: 3 }}>
                  <Check lab="Create (insert features)" dis />
                  <Check lab="Update (modify features)" dis />
                  <Check lab="Delete features" dis />
                  <Check lab="Editing attachments" dis />
                </div>
              </Row>
              <Row label="Sync / replicas" hint="for offline-edits workflow (Esri Collector etc.)"><Check lab="enabled" dis /></Row>
              <Row label="HTML in attribute responses" hint="some clients render this; security risk for public services"><Check lab="allowed" /></Row>
            </Group>

            {/* OUTPUT FORMATS */}
            <Group title="Output formats">
              <Row label="Feature query response">
                <div className="row" style={{ gap: 4, flexWrap: 'wrap', fontSize: 11 }}>
                  <Check on lab="JSON" />
                  <Check on lab="GeoJSON" />
                  <Check on lab="PBF · vector tile" />
                  <Check lab="AMF (legacy Flex)" />
                  <Check lab="HTML" />
                </div>
              </Row>
              <Row label="GetFeatureInfo response">
                <div className="row" style={{ gap: 4, flexWrap: 'wrap', fontSize: 11 }}>
                  <Check on lab="HTML" />
                  <Check on lab="JSON" />
                  <Check on lab="XML" />
                </div>
              </Row>
              <Row label="Date format in JSON" hint="ISO-8601 vs Unix ms — clients vary"><Sel value="ISO-8601 (recommended)" /></Row>
              <Row label="Decimals to preserve">
                <Inp mono value="6" />
              </Row>
            </Group>

            {/* CACHING */}
            <Group title="Caching">
              <Row label="Default cache TTL" hint="how long responses stay fresh; layers can override"><Sel value="30 min" /></Row>
              <Row label="Cache control header"><Sel value="public, max-age=1800, stale-while-revalidate=300" /></Row>
              <Row label="Cache key includes"><div className="col" style={{ gap: 3 }}><Check on lab="bbox" /><Check on lab="where clause" /><Check on lab="output format" /><Check lab="authenticated user (per-user cache)" /></div></Row>
              <Row label="Tile cache" hint="WMTS-style tile pyramid — n/a for FeatureServer query responses"><span className="muted">— not applicable for FeatureServer —</span></Row>
            </Group>

            {/* ACCESS */}
            <Group title="Access" sub="see Access tab for full grants + audiences">
              <Row label="Anonymous read" hint="folder default = public read"><Check on lab="enabled (inherits from folder)" /></Row>
              <Row label="HTTPS only"><Check on lab="enforced" /></Row>
              <Row label="Rate limit" hint="per IP for anonymous; per token for authenticated">
                <div className="row" style={{ gap: 6 }}><Inp mono value="60" /><span className="muted">req / 10s</span></div>
              </Row>
              <Row label="CORS · origins" hint="overrides server-wide CORS for this service">
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                  <span className="tag" style={{ background: '#fff', border: '1px solid #d0d0d0' }}>* (any · GET, HEAD)</span>
                  <span className="tag" style={{ background: '#fff', border: '1px solid #d0d0d0' }}>https://maps.partner.gov (with credentials)</span>
                  <Btn ghost sm>+ Origin</Btn>
                </div>
              </Row>
              <Row label="Allow embedding" hint="lets external sites render previews from this service">
                <Sel value="Same-origin + listed embed hosts" />
              </Row>
            </Group>

            {/* INTEGRATIONS */}
            <Group title="Catalog registration">
              <Row label="Register in Esri catalog" hint="default on for FeatureServer · keeps title, description, thumbnail in sync · uncheck to keep service live but uncataloged">
                <div className="row" style={{gap:8}}>
                  <label className="row" style={{gap:6, fontSize:11}}>
                    <input type="checkbox" readOnly defaultChecked />
                    <span>enabled</span>
                  </label>
                  <a style={{fontSize:11, color:'var(--pencil)', textDecoration:'underline dotted', cursor:'pointer'}}>Open in Esri catalog ↗</a>
                  <span className="muted mono" style={{fontSize:10}}>/catalog/item/a3bf…0214</span>
                </div>
              </Row>
              <Row label="Register in OGC Records" hint="only OGC API Features triggers this"><span className="muted">— n/a for FeatureServer —</span></Row>
              <Row label="Webhook on publish" hint="HTTP POST to your endpoint when a layer is published"><Sel value="analytics-bi (configured)" /></Row>
              <Row label="Audit log retention"><Sel value="90 days" /></Row>
            </Group>

            {/* SCHEDULE */}
            <Group title="Schedule">
              <Row label="Service maintenance window" hint="layer publishes & cache warms queue here"><Sel value="Daily · 02:00 – 04:00 UTC" /></Row>
              <Row label="Auto-stop if degraded for"><Sel value="never" /></Row>
              <Row label="Health check interval"><Sel value="30 s" /></Row>
            </Group>
          </div>

          {/* RIGHT COL */}
          <div className="col">
            <Callout kind="info">
              <b>Some settings restart the service.</b> Changes to CRS, route, capabilities, and output formats interrupt running queries for ~3 s. Cache, limits, access, and integrations are hot-applied.
            </Callout>

            <div className="card" style={{ background: '#fffdf3', borderLeft: '3px solid var(--accent-deep)' }}>
              <h3>What inherits to layers</h3>
              <div className="col" style={{ gap: 4, fontSize: 11 }}>
                <div className="row"><span style={{ flex: 1 }}>Default CRS</span><Badge>inherited</Badge></div>
                <div className="row"><span style={{ flex: 1 }}>Max record count</span><Badge>inherited · overridable</Badge></div>
                <div className="row"><span style={{ flex: 1 }}>Cache TTL</span><Badge>inherited · overridable</Badge></div>
                <div className="row"><span style={{ flex: 1 }}>Output formats</span><Badge>inherited · overridable</Badge></div>
                <div className="row"><span style={{ flex: 1 }}>Capabilities</span><Badge>per-layer toggle</Badge></div>
                <div className="row"><span style={{ flex: 1 }}>Anonymous access</span><Badge>inherited from folder</Badge></div>
              </div>
            </div>

            <div className="card">
              <h3>Current health</h3>
              <dl className="kv">
                <dt>Uptime</dt><dd>14d 06h</dd>
                <dt>p95 latency</dt><dd className="mono">184 ms</dd>
                <dt>Requests / hr</dt><dd className="mono">8,420</dd>
                <dt>Cache hit rate</dt><dd className="mono">72%</dd>
                <dt>Error rate</dt><dd className="mono">0.02%</dd>
              </dl>
            </div>

            <div className="card">
              <h3>Recent settings changes</h3>
              <div className="col" style={{ gap: 4, fontSize: 11 }}>
                <div className="row" style={{ padding: '3px 0', borderBottom: '1px dashed #eee' }}><span style={{ flex: 1 }}>cache TTL 60 → 30 min</span><span className="muted">2m · jamie</span></div>
                <div className="row" style={{ padding: '3px 0', borderBottom: '1px dashed #eee' }}><span style={{ flex: 1 }}>+ CORS origin maps.partner.gov</span><span className="muted">3d · k.tan</span></div>
                <div className="row" style={{ padding: '3px 0', borderBottom: '1px dashed #eee' }}><span style={{ flex: 1 }}>output format · enabled PBF</span><span className="muted">2w · jamie</span></div>
                <div className="row" style={{ padding: '3px 0', borderBottom: '1px dashed #eee' }}><span style={{ flex: 1 }}>standardized queries enforced</span><span className="muted">3w · jamie</span></div>
              </div>
            </div>

            <Ann red>service-kind specific settings: WMTS has tile-matrix sets, WMS has SLD support &amp; format list, OGC API has conformance classes, OData has $expand/$batch.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ServicesList, ServicesExplorer, ServiceDetail, ServiceRuntimeSettings, PublishMatrixA, PublishMatrixB });
