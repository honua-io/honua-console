// Catalogs section — top-level surface for discovery endpoints.
// Three screens: CatalogsList, EsriCatalogDetail, CatalogItemEditor.

function CatalogsList() {
  return (
    <div className="scr">
      <TopBar crumbs={['Catalogs']} />
      <Sidebar active="catalogs" />
      <div className="main">
        <PageHead
          title="Catalogs"
          sub={<span>Discovery endpoints consumers browse to find your data. <span className="muted">Server-wide on/off lives in <a className="mono" style={{color:'var(--pencil)'}}>Settings → Catalog endpoints</a>. Per-service registration is in each service's settings.</span></span>}
        />
        <Toolbar
          filters={<>
            <FiltChip on x>status: any</FiltChip>
            <FiltChip>has issues</FiltChip>
          </>}
          right={<span className="muted" style={{fontSize:11}}>2 catalogs auto-default-on · 3 opt-in</span>}
        />

        <div style={{padding:'14px 18px', overflow:'auto', flex:1, display:'grid', gridTemplateColumns:'repeat(2, 1fr)', gap:14}}>
          {[
            {
              k:'esri', t:'Esri catalog',
              d:'Esri-style catalog. Items are FeatureServer / MapServer / ImageServer publications.',
              on:true, url:'/catalog', n:38, fed:'auto-mirror from Esri services',
              feeders:[ ['FeatureServer','public-works-fs · 8'], ['MapServer','public-works-ms · 8'], ['FeatureServer','fs-internal · 24'] ],
              issues: 1, primary:true,
              auto:true,
            },
            {
              k:'ogc', t:'OGC API Records',
              d:'OGC Records. Records are OGC API Features collections.',
              on:true, url:'/records', n:38, fed:'auto-mirror from OGC API Features',
              feeders:[ ['OGC API Features','features-public · 38'] ],
              issues: 0, primary:true,
              auto:true,
            },
            {
              k:'odata', t:'OData catalog',
              d:'Service document + $metadata that lets BI tools discover entity sets.',
              on:true, url:'/odata', n:3, fed:'opt-in per entity set',
              feeders:[ ['OData','odata-bi · 3 of 8 entity sets opted in'] ],
              issues: 0, primary:true,
              auto:false,
            },
            {
              k:'stac', t:'STAC',
              d:'SpatioTemporal Asset Catalog. Raster-leaning. Opt-in per resource.',
              on:false, url:'/stac', n:0, fed:'opt-in per resource',
              feeders:[],
              issues: 0, primary:true,
              auto:false,
            },
            {
              k:'dcat', t:'DCAT',
              d:'Open-data catalog. Used by national / EU portals. Opt-in per resource.',
              on:false, url:'/dcat', n:0, fed:'opt-in per resource',
              feeders:[],
              issues: 0, primary:true,
              auto:false,
            },
          ].map(c => (
            <div key={c.k} className="card" style={{padding:0}}>
              {/* header */}
              <div style={{padding:'10px 14px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center', gap:10, background: c.on ? '#fff' : '#fafafa'}}>
                <span style={{fontSize:18, color:'#666'}}>⌘</span>
                <div style={{flex:1}}>
                  <div className="row" style={{gap:6}}>
                    <b style={{fontSize:13}}>{c.t}</b>
                    {c.on ? <Badge kind="ok">ON</Badge> : <Badge>OFF</Badge>}
                    {c.auto ? <Badge kind="accent">auto-default</Badge> : <Badge>opt-in</Badge>}
                    {c.issues > 0 && <Badge kind="warn">{c.issues} issue{c.issues > 1 ? 's' : ''}</Badge>}
                  </div>
                  <div className="muted" style={{fontSize:11, marginTop:2}}>{c.d}</div>
                </div>
              </div>

              {/* body */}
              {c.on ? (
                <>
                  <div style={{padding:'10px 14px'}}>
                    <div style={{display:'grid', gridTemplateColumns:'80px 1fr', rowGap:5, columnGap:10, fontSize:11.5}}>
                      <span className="muted">Endpoint</span>
                      <div className="row" style={{gap:6}}>
                        <code className="mono" style={{flex:1, background:'#fafafa', border:'1px solid #e4e4e4', padding:'2px 6px', borderRadius:3, fontSize:11}}>
                          https://honua.example.gov{c.url}
                        </code>
                        <Btn ghost sm>⧉</Btn>
                        <Btn sm>Open ↗</Btn>
                      </div>
                      <span className="muted">Entries</span>
                      <span><b>{c.n}</b></span>
                      <span className="muted">Fed by</span>
                      <div className="col" style={{gap:2}}>
                        {c.feeders.map((f,i) => (
                          <div key={i} className="row" style={{fontSize:10.5}}>
                            <span className="tag">{f[0]}</span>
                            <span className="mono" style={{color:'#666'}}>{f[1]}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  </div>
                  <div style={{padding:'6px 14px', borderTop:'1px dashed #eee', background:'#fafafa', display:'flex', gap:6}}>
                    <Btn sm>Browse items →</Btn>
                    <Btn ghost sm>Settings</Btn>
                    <Btn ghost sm>Rebuild index</Btn>
                  </div>
                </>
              ) : (
                <div style={{padding:'12px 14px', fontSize:11.5}}>
                  <Callout kind="info" style={{margin:0}}>
                    <b>Endpoint disabled.</b> Turn on in <a className="mono" style={{color:'var(--pencil)'}}>Settings → Catalog endpoints</a> to expose it to consumers. {c.auto ? 'Already-registered entries will resume on re-enable.' : 'Resources can opt in once enabled.'}
                  </Callout>
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function EsriCatalogDetail() {
  // Detail page for one catalog — Esri.
  // Tabs: Items / Settings / Activity. Items table is the meat.
  return (
    <div className="scr">
      <TopBar crumbs={['Catalogs','Esri catalog']} />
      <Sidebar active="catalogs" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Catalogs <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>Esri catalog</h1>
            <Badge kind="ok" lg>ON</Badge>
            <Badge kind="warn">1 issue</Badge>
            <span className="muted" style={{fontSize:11}}>/catalog · 38 items · last rebuild 14m ago</span>
            <div style={{flex:1}}/>
            <Btn ghost>⧉ Copy</Btn>
            <Btn>Open ↗</Btn>
            <Btn>Rebuild index</Btn>
          </div>
          <div style={{marginTop:8, padding:'8px 10px', background:'#fafafa', border:'1px solid #e4e4e4', borderRadius:5, display:'flex',alignItems:'center', gap:8}}>
            <span className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Endpoint</span>
            <code className="mono" style={{flex:1,fontSize:11,color:'#333'}}>https://honua.example.gov/catalog</code>
            <Btn ghost sm>⧉ Copy URL</Btn>
            <Btn ghost sm>Open in ArcGIS Pro ↗</Btn>
          </div>
        </div>
        <Tabs items={[
          { k:'items', t:'Items', ct: 38 },
          { k:'settings', t:'Settings' },
          { k:'access', t:'Access' },
          { k:'activity', t:'Activity' },
          { k:'validation', t:'Validation', ct: 1 },
        ]} active="items" />

        <Toolbar
          filters={<>
            <FiltChip on x>origin: any</FiltChip>
            <FiltChip>has thumbnail</FiltChip>
            <FiltChip>has issue</FiltChip>
            <FiltChip>tag: any</FiltChip>
            <input className="inp" style={{width:200, height:22}} placeholder="Search 38 items…" readOnly />
          </>}
          right={<>
            <ScopeChip scope="resource" />
            <Btn ghost sm>Columns</Btn>
            <Btn ghost sm>Export JSON</Btn>
          </>}
        />

        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th style={{width:24}}><input type="checkbox" readOnly /></th>
              <th style={{width:42}}>Thumb</th>
              <th>Title</th>
              <th>Item ID</th>
              <th>From service</th>
              <th>Resource</th>
              <th>Tags</th>
              <th>Thumbnail</th>
              <th>License</th>
              <th>Issues</th>
              <th>Updated</th>
            </tr></thead>
            <tbody>
              {[
                { thumb:'#d9a23a', title:'Parcels (FY 2024)', id:'a3bf…0214', svc:'public-works-fs / 0', res:'parcels_2024',
                  tags:['parcels','cadastre'], hasThumb:true, license:'CC-BY 4.0', iss:0, upd:'2m' },
                { thumb:'#d9a23a', title:'Parcels (FY 2024)', id:'a3bf…0214', svc:'public-works-ms / 2', res:'parcels_2024',
                  tags:['parcels','cadastre'], hasThumb:true, license:'CC-BY 4.0', iss:0, upd:'2m', shared:true },
                { thumb:'#7a7a7a', title:'Road centerlines', id:'b4c8…7720', svc:'public-works-fs / 1', res:'roads_osm',
                  tags:['roads','transport'], hasThumb:true, license:'ODbL 1.0', iss:0, upd:'3d' },
                { thumb:'#c84a30', title:'Hydrants',         id:'c5d9…9012', svc:'public-works-fs / 2', res:'hydrants_2024',
                  tags:['hydrants','public-works'], hasThumb:true, license:'CC-BY 4.0', iss:0, upd:'2d' },
                { thumb:'#7ca7b3', title:'Wetlands',         id:'d6ea…3344', svc:'public-works-fs / 3', res:'wetlands_2025',
                  tags:['wetlands','environment'], hasThumb:true, license:'CC-BY 4.0', iss:0, upd:'14m' },
                { thumb:'#aa3a2b', title:'Fire perimeters',  id:'e7fb…5566', svc:'public-works-fs / 4', res:'fire_perimeters',
                  tags:['fire','hazards'], hasThumb:false, license:'public domain', iss:1, upd:'28m' },
                { thumb:'#4a8b6f', title:'Watersheds',       id:'f80c…7788', svc:'public-works-fs / 5', res:'watersheds_v3',
                  tags:['watersheds','environment'], hasThumb:true, license:'CC-BY 4.0', iss:0, upd:'2h' },
                { thumb:'#2a6fdb', title:'Observation sites',id:'091d…9900', svc:'public-works-fs / 6', res:'obs_stations',
                  tags:['sensors','monitoring'], hasThumb:true, license:'CC-BY 4.0', iss:0, upd:'1h' },
                { thumb:'#888',    title:'Sentinel-2 imagery', id:'1a2e…aabb', svc:'imagery-is / 0', res:'sentinel_2_tiles',
                  tags:['imagery','raster'], hasThumb:true, license:'CC-BY 4.0', iss:0, upd:'4h' },
              ].map((r,i) => (
                <tr key={i} style={r.iss > 0 ? {background:'#fff7e6'} : (r.shared ? {background:'#fafafa'} : null)}>
                  <td><input type="checkbox" readOnly /></td>
                  <td>
                    {r.hasThumb
                      ? <div style={{width:28, height:20, background:r.thumb, border:'1px solid #ccc', borderRadius:2}} />
                      : <div style={{width:28, height:20, background:'repeating-linear-gradient(135deg,#f3f3f3 0 4px,#e8e8e8 4px 8px)', border:'1px dashed #c5c5c5', borderRadius:2, display:'grid', placeItems:'center', fontSize:8, color:'#999'}}>?</div>}
                  </td>
                  <td><b>{r.title}</b>{r.shared && <span className="muted" style={{marginLeft:6, fontSize:10}}>(same item as FS/0)</span>}</td>
                  <td className="mono" style={{fontSize:10.5, color:'#666'}}>{r.id}</td>
                  <td><span className="mono" style={{fontSize:10.5}}>{r.svc}</span></td>
                  <td><span style={{color:'var(--pencil)'}}>◇</span> <a className="mono" style={{color:'var(--pencil)', fontSize:10.5}}>{r.res}</a></td>
                  <td>{r.tags.map(t => <span key={t} className="tag" style={{marginRight:2}}>#{t}</span>)}</td>
                  <td>{r.hasThumb ? <Badge kind="ok">✓</Badge> : <Badge kind="warn">missing</Badge>}</td>
                  <td className="muted" style={{fontSize:10.5}}>{r.license}</td>
                  <td>{r.iss > 0 ? <Badge kind="warn">{r.iss}</Badge> : <span className="muted">—</span>}</td>
                  <td className="muted">{r.upd}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div style={{padding:'6px 18px', borderTop:'1px solid #e4e4e4', background:'#fafafa', display:'flex',alignItems:'center', gap:8, fontSize:11}}>
          <span className="muted">Auto-mirror is on. Every Esri service publication appears here unless explicitly unchecked.</span>
          <div style={{flex:1}}/>
          <Btn ghost sm>Bulk edit metadata defaults</Btn>
        </div>
      </div>
    </div>
  );
}

function CatalogItemEditor() {
  // Single catalog item · for an auto-mirrored entry.
  // Most fields are 🧮 derived from the service/resource. A few are ✏️ catalog-only (tags, featured, related items).
  return (
    <div className="scr">
      <TopBar crumbs={['Catalogs','Esri catalog','Parcels (FY 2024)']} />
      <Sidebar active="catalogs" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Esri catalog <span style={{color:'#bbb'}}>/</span> item <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>Parcels (FY 2024)</h1>
            <Badge kind="info">auto-mirror</Badge>
            <Badge kind="ok">live</Badge>
            <span className="muted" style={{fontSize:11}}>item a3bf…0214 · 2 backing services</span>
            <div style={{flex:1}}/>
            <Btn ghost>⧉ Copy item URL</Btn>
            <Btn ghost>Preview ↗</Btn>
            <Btn kind="p">↗ Open Data Resource</Btn>
          </div>
        </div>
        <FieldStateLegend />

        <div style={{padding:'14px 18px', overflow:'auto', flex:1, display:'grid', gridTemplateColumns:'1fr 320px', gap:14}}>
          <div className="col">

            <Callout kind="info">
              <b>Auto-mirror.</b> Most fields below come straight from the underlying Data Resource. The catalog adds a thin layer of presentation: tags, thumbnail, featured flag, related items. Edit the canonical fields in the resource and they propagate here.
            </Callout>

            <FieldGroup title="Identity" sub="from the resource · read-only here" scope="resource" count="2 layers bind this resource">
              <FieldRow state="system"     label="Item ID"       value="a3bf-2024-0214-cdef-7820" />
              <FieldRow state="calculated" label="Title"         value="Parcels (FY 2024)"  derivedFrom="Resource.Metadata.Title" />
              <FieldRow state="calculated" label="Slug"          value="parcels-fy-2024"   derivedFrom="Resource.Metadata.Name" />
              <FieldRow state="calculated" label="Description"   value="Tax parcel boundaries for fiscal year 2024…" derivedFrom="Resource.Metadata.Description" />
              <FieldRow state="calculated" label="Owner"         value="state-gis"         derivedFrom="Resource.Metadata.Publisher" />
              <FieldRow state="calculated" label="License"       value="CC-BY 4.0"         derivedFrom="Resource.Metadata.License" />
            </FieldGroup>

            <FieldGroup title="Catalog presentation" sub="lives only in the catalog · doesn't touch the resource" scope="publication">
              <FieldRow state="input" label="Display thumbnail" sub="shown in catalog search results">
                <div className="row" style={{gap:6}}>
                  <div style={{width:60, height:42, background:'#d9a23a', border:'1px solid #999', borderRadius:3}} />
                  <div className="col" style={{gap:2}}>
                    <Btn ghost sm>Upload…</Btn>
                    <Btn ghost sm>Auto-render from map</Btn>
                  </div>
                  <span className="muted" style={{fontSize:10.5}}>currently: auto-rendered from layer 0 · 12 May 2026</span>
                </div>
              </FieldRow>
              <FieldRow state="input" label="Tags" sub="catalog-only tags · resource keywords are separate">
                <div className="row" style={{flexWrap:'wrap', gap:4}}>
                  {['parcels','cadastre','assessor','FY2024'].map(t => <span key={t} className="tag">#{t} ×</span>)}
                  <Btn ghost sm>+ Tag</Btn>
                </div>
              </FieldRow>
              <FieldRow state="input" label="Featured" sub="show on catalog home page">
                <label className="row" style={{gap:6, fontSize:11}}>
                  <input type="checkbox" readOnly />
                  <span>not featured</span>
                </label>
              </FieldRow>
              <FieldRow state="input" label="Related items"     hint="cross-reference other catalog items">
                <div className="row" style={{flexWrap:'wrap',gap:4}}>
                  {['Road centerlines','Hydrants'].map(t => <span key={t} className="tag" style={{cursor:'pointer'}}>{t} ×</span>)}
                  <Btn ghost sm>+ Related</Btn>
                </div>
              </FieldRow>
            </FieldGroup>

            <FieldGroup title="Service bindings" sub="which services feed this catalog entry" scope="service">
              <FieldRow state="system" label="Primary service"   value="public-works-fs / layer 0" />
              <FieldRow state="system" label="Other bindings"    value="public-works-ms / layer 2 (same resource)" />
              <FieldRow state="calculated" label="Service URLs" derivedFrom="bindings">
                <div className="col" style={{gap:2}}>
                  <div className="row" style={{gap:6, fontSize:10.5}}>
                    <span className="mono" style={{flex:1, color:'#555'}}>/public/pw/FeatureServer/0</span>
                    <Btn ghost sm>⧉</Btn>
                  </div>
                  <div className="row" style={{gap:6, fontSize:10.5}}>
                    <span className="mono" style={{flex:1, color:'#555'}}>/public/pw/MapServer/2</span>
                    <Btn ghost sm>⧉</Btn>
                  </div>
                </div>
              </FieldRow>
            </FieldGroup>

            <FieldGroup title="Standards mapping" sub="how this item appears in different catalog dialects" scope="server">
              <FieldRow state="calculated" label="Esri item JSON" value="schema valid · 14 KB" derivedFrom="resource + service" />
              <FieldRow state="calculated" label="ISO 19115 record" value="schema valid · 38 KB" derivedFrom="resource metadata" />
              <FieldRow state="calculated" label="DCAT dataset" value="missing publisher identifier" derivedFrom="resource metadata">
                <div className="col" style={{gap:2}}>
                  <span className="mono" style={{fontSize:11, color:'#555'}}>schema valid</span>
                  <span style={{fontSize:10.5, color:'var(--warn)'}}>· missing publisher identifier for DCAT</span>
                  <a className="fs-override" style={{fontSize:10}}>fix on resource →</a>
                </div>
              </FieldRow>
            </FieldGroup>
          </div>

          {/* RIGHT */}
          <div className="col">
            <div className="card">
              <h3>Quick facts</h3>
              <dl className="kv">
                <dt>Item ID</dt><dd className="mono" style={{fontSize:10}}>a3bf-2024-0214</dd>
                <dt>Backed by</dt><dd className="mono">◇ parcels_2024</dd>
                <dt>Services</dt><dd>FeatureServer/0 · MapServer/2</dd>
                <dt>Tags</dt><dd>4</dd>
                <dt>Thumbnail</dt><dd>auto-rendered · 12 May</dd>
                <dt>License</dt><dd>CC-BY 4.0</dd>
              </dl>
            </div>
            <Callout kind="warn">
              <b>1 standards issue.</b> DCAT requires a publisher identifier on the resource. Currently empty. <a className="fs-override" style={{fontSize:11}}>Fix on resource →</a>
            </Callout>
            <Ann red>auto-mirror items can't fork their identity. uncheck "register in Esri catalog" on the service to remove this entry.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { CatalogsList, EsriCatalogDetail, CatalogItemEditor });
