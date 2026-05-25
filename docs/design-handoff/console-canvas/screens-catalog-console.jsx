// Honua Console · Catalog surface — content discovery, inspection, lineage, versions.
// Catalog handles every content type uniformly: data resources, maps, dashboards,
// reports, forms, apps, workflows, queries, analyses, GP services, ETL pipelines, connectors, templates.
//
// 3 screens:
//   CatalogList         — unified browse across content types
//   ContentItemDetail   — works for any content type (here: map)
//   ContentItemUsage    — "where used" lineage view

function CatalogList() {
  return (
    <div className="scr">
      <TopBar crumbs={['Catalog']} area="catalog" />
      <Sidebar active="catalogs" />
      <div className="main">
        <PageHead
          title="Catalog"
          sub="Everything your workspace owns — data, maps, dashboards, forms, apps, workflows, analyses, GP services. Versioned, governed, reusable."
          actions={<>
            <Btn>Import…</Btn>
            <Btn kind="p">+ New from Studio</Btn>
          </>}
        />

        <Toolbar
          filters={<>
            <FiltChip on x>type: any</FiltChip>
            <FiltChip>status: published</FiltChip>
            <FiltChip>owner: me</FiltChip>
            <FiltChip>tags: any</FiltChip>
            <FiltChip>has issues</FiltChip>
            <input className="inp" style={{width:200, height:22}} placeholder="Search content…" readOnly />
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>284 items · 12 shown</span>
            <Btn ghost sm>Group by type</Btn>
            <Btn ghost sm>Columns</Btn>
          </>}
        />

        {/* Type strip — counts as filter chips */}
        <div style={{padding:'8px 18px', borderBottom:'1px solid #e4e4e4', background:'#fff', display:'flex', alignItems:'center', gap:6, fontSize:11, flexWrap:'wrap'}}>
          {[
            ['◇','Data resources','128'],
            ['◐','Maps','42'],
            ['▤','Dashboards','18'],
            ['⊟','Reports','7'],
            ['☱','Forms','9'],
            ['❒','Apps','4'],
            ['◇','Queries','21'],
            ['∑','Analyses','14'],
            ['⇋','Workflows','11'],
            ['⚙','GP services','6'],
            ['⤴','ETL pipelines','8'],
            ['🔌','Connectors','6'],
            ['📋','Templates','10'],
          ].map(([gl,t,n]) => (
            <span key={t} className="tag" style={{padding:'3px 8px', display:'inline-flex', alignItems:'center', gap:4, cursor:'pointer'}}>
              <span style={{color:'#666'}}>{gl}</span>
              <b>{t}</b>
              <span className="muted">{n}</span>
            </span>
          ))}
        </div>

        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th style={{width:24}}><input type="checkbox" readOnly /></th>
              <th>Title</th>
              <th>Type</th>
              <th>Version</th>
              <th>Status</th>
              <th>Published to</th>
              <th>Owner</th>
              <th>Modified</th>
              <th>Used by</th>
              <th>Validation</th>
            </tr></thead>
            <tbody>
              {[
                { t:'parcels_2024', kind:'data', ico:'◇', ver:'v4', st:'ok',  pub:'OGC API · Esri', own:'jamie', mod:'2h', used:6, val:'ok' },
                { t:'Parcels heatmap (FY24)', kind:'map', ico:'◐', ver:'v4', st:'ok', pub:'Public', own:'jamie', mod:'2h', used:3, val:'ok' },
                { t:'Q3 land use review', kind:'dashboard', ico:'▤', ver:'draft', st:'draft', pub:'—', own:'jamie', mod:'5h', used:0, val:'ok' },
                { t:'Hydrant maintenance', kind:'form', ico:'☱', ver:'v2', st:'ok', pub:'Field team', own:'k.tan', mod:'1d', used:1, val:'ok' },
                { t:'Fire perimeter trend', kind:'dashboard', ico:'▤', ver:'draft', st:'warn', pub:'—', own:'jamie', mod:'3d', used:0, val:'warn' },
                { t:'Census tract aggregation', kind:'analysis', ico:'∑', ver:'v1', st:'ok', pub:'Catalog', own:'a.lee', mod:'5d', used:2, val:'ok' },
                { t:'Wetlands inspector app', kind:'app', ico:'❒', ver:'v3', st:'ok', pub:'Public · embed', own:'k.tan', mod:'1w', used:0, val:'ok' },
                { t:'Public works overview', kind:'report', ico:'⊟', ver:'v2', st:'ok', pub:'Workspace', own:'jamie', mod:'1w', used:0, val:'ok' },
                { t:'Daily refresh · parcels', kind:'workflow', ico:'⇋', ver:'v1', st:'ok', pub:'Scheduled', own:'system', mod:'2w', used:1, val:'ok' },
                { t:'Parcels feature query', kind:'query', ico:'◇', ver:'v1', st:'ok', pub:'Reusable', own:'jamie', mod:'2w', used:8, val:'ok' },
                { t:'Buffer + intersect GP', kind:'gp', ico:'⚙', ver:'v2', st:'ok', pub:'Public', own:'a.lee', mod:'3w', used:4, val:'ok' },
                { t:'AssessorLink', kind:'connector', ico:'🔌', ver:'v1', st:'ok', pub:'Workspace', own:'system', mod:'4w', used:14, val:'ok' },
              ].map((r,i) => (
                <tr key={i}>
                  <td><input type="checkbox" readOnly /></td>
                  <td><span style={{color:'#666',marginRight:6}}>{r.ico}</span><b>{r.t}</b></td>
                  <td><span className="tag">{r.kind}</span></td>
                  <td className="mono">{r.ver}</td>
                  <td>
                    {r.st === 'ok' && <Badge kind="ok">published</Badge>}
                    {r.st === 'draft' && <Badge>draft</Badge>}
                    {r.st === 'warn' && <Badge kind="warn">validation</Badge>}
                  </td>
                  <td className="muted">{r.pub}</td>
                  <td className="mono">{r.own}</td>
                  <td className="muted">{r.mod}</td>
                  <td>{r.used > 0 ? <b>{r.used}</b> : <span className="muted">—</span>}</td>
                  <td>{r.val === 'warn' ? <Badge kind="warn">1</Badge> : <Badge kind="ok">✓</Badge>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function ContentItemDetail() {
  // Detail for any content item. Showing: Parcels heatmap (map).
  // Tabs: Overview / Versions / Lineage / Bindings / Publication / Permissions / Activity
  return (
    <div className="scr">
      <TopBar crumbs={['Catalog','Parcels heatmap (FY24)']} area="catalog" />
      <Sidebar active="catalogs" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Catalog <span style={{color:'#bbb'}}>/</span> Maps <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <span style={{fontSize:18, color:'#666'}}>◐</span>
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>Parcels heatmap (FY24)</h1>
            <Badge kind="ok" lg>published v4</Badge>
            <span className="muted" style={{fontSize:11}}>map · workspace Public Works · public read</span>
            <div style={{flex:1}}/>
            <Btn ghost>⧉ Share link</Btn>
            <Btn ghost>Open in Studio →</Btn>
            <Btn>Embed code</Btn>
            <Btn kind="p">+ New version</Btn>
          </div>
          <div className="muted" style={{fontSize:11.5, marginTop:6}}>
            Statewide parcels coloured by use code. Sealed boundary, public read. Last published 2h ago.
          </div>
        </div>

        <Tabs items={[
          { k:'overview', t:'Overview' },
          { k:'versions', t:'Versions', ct: 4 },
          { k:'lineage', t:'Lineage' },
          { k:'bindings', t:'Data bindings', ct: 1 },
          { k:'publication', t:'Publication' },
          { k:'permissions', t:'Permissions' },
          { k:'activity', t:'Activity' },
        ]} active="overview" />

        <div style={{display:'grid', gridTemplateColumns:'1fr 320px', flex:1, overflow:'hidden'}}>
          {/* MAIN */}
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            {/* Preview */}
            <div className="card" style={{padding:0, marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center'}}>
                <h3 style={{margin:0}}>Preview</h3>
                <div style={{flex:1}}/>
                <Btn ghost sm>Open full size ↗</Btn>
              </div>
              <div style={{padding:10}}>
                <MapPreview mode="layer" height={280} popup={true} scaleText="1:8,000" />
              </div>
            </div>

            {/* Versions */}
            <div className="card" style={{padding:0, marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center'}}>
                <h3 style={{margin:0}}>Versions</h3>
                <span className="muted" style={{marginLeft:8, fontSize:11}}>4 versions · current is v4</span>
                <div style={{flex:1}}/>
                <Btn ghost sm>Compare versions</Btn>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>Version</th><th>State</th><th>Author</th><th>Published</th><th>Change notes</th><th></th></tr></thead>
                <tbody>
                  <tr style={{background:'#fffae0'}}>
                    <td className="mono"><b>v4</b></td>
                    <td><Badge kind="ok">published · current</Badge></td>
                    <td className="mono">jamie</td>
                    <td className="muted">2h ago</td>
                    <td>tightened use-code colour palette · added zoom 14 labels</td>
                    <td><a className="fs-override" style={{fontSize:10.5}}>View</a></td>
                  </tr>
                  <tr>
                    <td className="mono">v3</td>
                    <td><Badge>superseded</Badge></td>
                    <td className="mono">k.tan</td>
                    <td className="muted">5d ago</td>
                    <td>added PII redaction to popup template</td>
                    <td><div className="row" style={{gap:4, fontSize:10.5}}><a style={{cursor:'pointer'}}>View</a> · <a style={{cursor:'pointer'}}>Rollback</a></div></td>
                  </tr>
                  <tr><td className="mono">v2</td><td><Badge>superseded</Badge></td><td className="mono">jamie</td><td className="muted">2w</td><td>class breaks renderer</td><td><a style={{fontSize:10.5,cursor:'pointer'}}>View</a></td></tr>
                  <tr><td className="mono">v1</td><td><Badge>superseded</Badge></td><td className="mono">jamie</td><td className="muted">4w</td><td>initial draft</td><td><a style={{fontSize:10.5,cursor:'pointer'}}>View</a></td></tr>
                </tbody>
              </table>
            </div>

            {/* Lineage */}
            <div className="card" style={{padding:0, marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3 style={{margin:0}}>Lineage</h3>
                <div className="muted" style={{fontSize:10.5, marginTop:2}}>what this depends on (upstream) and what depends on it (downstream)</div>
              </div>
              <div style={{padding:'12px 18px', fontSize:11, lineHeight:1.7, fontFamily:'var(--mono)'}}>
                <div>↑ <b>upstream</b></div>
                <div style={{paddingLeft:16, color:'#666'}}>◇ parcels_2024 <span className="muted">· data resource v4</span></div>
                <div style={{paddingLeft:32, color:'#888'}}>↑ prod-postgis / public.parcels_2024 <span className="muted">· source</span></div>
                <div style={{paddingLeft:16, color:'#666'}}>📋 style · parcels-use <span className="muted">· canonical map style</span></div>
                <div style={{paddingLeft:16, color:'#666'}}>📋 basemap · positron <span className="muted">· basemap reference</span></div>
                <div style={{marginTop:8}}>● <b>this content</b> · Parcels heatmap (FY24) v4</div>
                <div style={{marginTop:8}}>↓ <b>downstream · 3 items</b></div>
                <div style={{paddingLeft:16, color:'#666'}}>▤ Q3 land use review <span className="muted">· dashboard (embedded)</span></div>
                <div style={{paddingLeft:16, color:'#666'}}>❒ Wetlands inspector app <span className="muted">· app (page 2)</span></div>
                <div style={{paddingLeft:16, color:'#666'}}>⊟ Public works overview <span className="muted">· report (figure 4)</span></div>
              </div>
            </div>
          </div>

          {/* SIDE */}
          <div style={{borderLeft:'1px solid #e4e4e4', padding:'14px 14px', overflow:'auto', background:'#fafafa'}}>
            <div className="card">
              <h3>Quick facts</h3>
              <dl className="kv">
                <dt>Type</dt><dd>Map</dd>
                <dt>Identifier</dt><dd className="mono">parcels-heatmap</dd>
                <dt>Owner</dt><dd>jamie</dd>
                <dt>Workspace</dt><dd>Public Works</dd>
                <dt>License</dt><dd>CC-BY 4.0</dd>
                <dt>Tags</dt><dd>parcels · cadastre · FY24</dd>
                <dt>Created</dt><dd>4w ago</dd>
                <dt>Modified</dt><dd>2h ago</dd>
              </dl>
            </div>

            <div className="card">
              <h3>Publication</h3>
              <dl className="kv">
                <dt>Visibility</dt><dd>Public</dd>
                <dt>URL</dt><dd className="mono" style={{fontSize:10}}>honua.example.gov/m/parcels-heatmap</dd>
                <dt>Embeddable</dt><dd>any origin</dd>
                <dt>Open data</dt><dd>opt-in · off</dd>
                <dt>In catalogs</dt><dd className="row" style={{gap:4,flexWrap:'wrap'}}><Badge kind="info">Esri catalog</Badge> <Badge kind="info">OGC Records</Badge></dd>
              </dl>
            </div>

            <Callout kind="info">
              <b>Used by 3 items.</b> Edit with care — changes propagate to dependents on next publish. Lineage tab shows the full chain.
            </Callout>

            <Ann red>v4 stays published until v5 supersedes it. rollback re-publishes v3.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function ContentItemUsage() {
  // "Where used" — same as Lineage tab but for a data resource (popular drill).
  // Lets a user see consequences before changing/retiring a resource.
  return (
    <div className="scr">
      <TopBar crumbs={['Catalog','parcels_2024','Lineage']} area="catalog" />
      <Sidebar active="catalogs" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Catalog <span style={{color:'#bbb'}}>/</span> Data resources <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <span style={{color:'var(--pencil)',fontSize:18}}>◇</span>
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>parcels_2024</h1>
            <Badge kind="ok" lg>v4 · published</Badge>
            <span className="muted" style={{fontSize:11}}>data resource · 1.28M features · MultiPolygon · 4326</span>
            <div style={{flex:1}}/>
            <Btn ghost>Preview</Btn>
            <Btn>Open in Studio →</Btn>
            <Btn kind="p">+ New version</Btn>
          </div>
        </div>

        <Tabs items={[
          { k:'overview', t:'Overview' },
          { k:'versions', t:'Versions', ct:4 },
          { k:'lineage', t:'Lineage', ct:9 },
          { k:'bindings', t:'Bindings' },
          { k:'publication', t:'Publication' },
          { k:'permissions', t:'Permissions' },
          { k:'activity', t:'Activity' },
        ]} active="lineage" />

        <div style={{padding:'10px 18px', background:'#fff7e6', borderBottom:'1.2px solid #e7c97a', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="warn">9 downstream items</Badge>
          <span style={{flex:1}}>
            Changing this resource's schema affects 3 maps, 2 dashboards, 1 app, and 3 service publications. Use <b>+ New version</b> instead of mutating the current canonical.
          </span>
          <Btn ghost sm>Preview impact</Btn>
        </div>

        <div style={{padding:'14px 18px', overflow:'auto', flex:1, display:'grid', gridTemplateColumns:'1fr 1fr', gap:12}}>
          {/* UPSTREAM */}
          <div className="card" style={{padding:0}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3>↑ Upstream · 2 sources</h3>
              <div className="muted" style={{fontSize:10.5}}>what this resource depends on</div>
            </div>
            <table className="tbl tbl--cmpt">
              <tbody>
                <tr><td>📦</td><td>prod-postgis</td><td><span className="mono" style={{fontSize:10}}>public.parcels_2024</span></td><td><Badge kind="ok">connected</Badge></td></tr>
                <tr><td>📋</td><td>State Assessor (origin)</td><td className="muted">external</td><td><Badge>provenance</Badge></td></tr>
              </tbody>
            </table>
          </div>

          {/* DOWNSTREAM */}
          <div className="card" style={{padding:0}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3>↓ Downstream · 9 items</h3>
              <div className="muted" style={{fontSize:10.5}}>what depends on this resource</div>
            </div>
            <table className="tbl tbl--cmpt">
              <thead><tr><th></th><th>Item</th><th>Type</th><th>Status</th></tr></thead>
              <tbody>
                <tr><td>◐</td><td>Parcels heatmap (FY24)</td><td>map</td><td><Badge kind="ok">v4</Badge></td></tr>
                <tr><td>◐</td><td>Parcels by county</td><td>map</td><td><Badge kind="ok">v1</Badge></td></tr>
                <tr><td>◐</td><td>Assessor inset map</td><td>map</td><td><Badge kind="ok">v2</Badge></td></tr>
                <tr><td>▤</td><td>Q3 land use review</td><td>dashboard</td><td><Badge>draft</Badge></td></tr>
                <tr><td>▤</td><td>Annual assessment dashboard</td><td>dashboard</td><td><Badge kind="ok">v3</Badge></td></tr>
                <tr><td>❒</td><td>Wetlands inspector app</td><td>app</td><td><Badge kind="ok">v3</Badge></td></tr>
                <tr><td>▤</td><td>public-works-fs · layer 0</td><td>service slot</td><td><Badge kind="ok">live</Badge></td></tr>
                <tr><td>▤</td><td>public-works-ms · layer 2</td><td>service slot</td><td><Badge kind="ok">live</Badge></td></tr>
                <tr><td>▤</td><td>features-public · parcels</td><td>service slot</td><td><Badge kind="ok">live</Badge></td></tr>
              </tbody>
            </table>
          </div>
        </div>

        <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', background:'#fafafa', fontSize:11}}>
          <span className="muted">Lineage powers impact preview before breaking changes. Schema-breaking edits show this same list as a confirmation.</span>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { CatalogList, ContentItemDetail, ContentItemUsage });
