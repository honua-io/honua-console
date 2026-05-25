// Honua Console · Studio surface — AI Map + AI Dashboard end-to-end flows
// 7 screens:
//   StudioHome            — workflow picker + recent projects
//   StudioMapAI           — prompt + conversation + clarification + package preview
//   StudioMapEditor       — full map editor with layers/style/filter/popup
//   StudioMapPublish      — publish review for the map content item
//   StudioDashboardAI     — prompt + dashboard package preview
//   StudioDashboardEditor — panel layout + Vega-Lite chart editor
//   StudioDashboardPublish

function StudioSidebar({ active }) {
  // Studio-specific sidebar — content-type oriented
  return (
    <aside className="side">
      <div style={{padding:'8px 12px 4px', display:'flex',alignItems:'center',gap:8}}>
        <div style={{
          width:22,height:22,border:'1.5px solid #141414',borderRadius:5,
          display:'grid',placeItems:'center',fontWeight:700,fontSize:11,background:'#ffe55c'
        }}>S</div>
        <div style={{fontWeight:700,fontSize:12,letterSpacing:'0.01em'}}>Studio</div>
      </div>
      <div style={{height:1, background:'#e4e4e4', margin:'6px 0'}} />
      <div className="grp">Recent</div>
      {[
        { t:'Parcels heatmap', k:'recent1', kind:'map' },
        { t:'Q3 land use review', k:'recent2', kind:'dashboard' },
        { t:'Hydrant maintenance', k:'recent3', kind:'form' },
      ].map(r => (
        <div key={r.k} className="it">
          <span className="gl">{r.kind === 'map' ? '◐' : r.kind === 'dashboard' ? '▤' : '☱'}</span>
          <span>{r.t}</span>
        </div>
      ))}
      <div className="grp">Create</div>
      {[
        { gl:'◐', t:'Map', k:'map', on: active === 'map' },
        { gl:'▤', t:'Dashboard', k:'dashboard', on: active === 'dashboard' },
        { gl:'⊟', t:'Report', k:'report' },
        { gl:'☱', t:'Form', k:'form' },
        { gl:'❒', t:'App', k:'app' },
        { gl:'◇', t:'Query', k:'query' },
        { gl:'∑', t:'Analysis', k:'analysis' },
        { gl:'⇋', t:'Workflow', k:'workflow' },
      ].map(it => (
        <div key={it.k} className={'it' + (it.on ? ' on' : '')}>
          <span className="gl">{it.gl}</span>
          <span>{it.t}</span>
        </div>
      ))}
      <div className="grp">From data</div>
      <div className="it"><span className="gl">◇</span><span>Browse Catalog…</span></div>
      <div className="it"><span className="gl">⤴</span><span>Import file…</span></div>
    </aside>
  );
}

function StudioHome() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Home']} area="studio" />
      <StudioSidebar />
      <div className="main">
        <PageHead
          title="Studio"
          sub="Create maps, dashboards, reports, forms, apps and analyses with AI. Inspectable, editable, publishable."
          actions={<><Btn>Import…</Btn><Btn kind="p">+ New from prompt</Btn></>}
        />

        <div style={{padding:'16px 24px', overflow:'auto', flex:1}}>
          {/* hero prompt */}
          <div style={{
            border:'1.5px solid var(--ink)', borderRadius:8, background:'#fff',
            padding:'14px 18px', marginBottom:18, maxWidth:840
          }}>
            <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>What do you want to build?</div>
            <textarea readOnly className="inp" style={{height:48, padding:8, fontSize:13, border:'none', resize:'none', background:'transparent'}} placeholder="e.g. Show parcels coloured by use code, with a popup showing assessed value…" />
            <div className="row" style={{gap:6, marginTop:6, flexWrap:'wrap'}}>
              <span className="muted" style={{fontSize:10.5}}>Suggestions:</span>
              {[
                'Map of parcels coloured by area',
                'Dashboard for land use trends',
                'Form for hydrant inspection',
                'Report comparing wetlands FY23 vs FY24',
                'Heatmap of fire observations',
              ].map(s => (
                <span key={s} className="tag" style={{cursor:'pointer'}}>{s}</span>
              ))}
              <div style={{flex:1}}/>
              <span className="muted" style={{fontSize:10}}>Or pick a type →</span>
              <Btn kind="p" sm>Send · ⌘↵</Btn>
            </div>
          </div>

          {/* content type grid */}
          <h3 style={{margin:'0 0 8px', font:'600 13px var(--ui)'}}>Start with a content type</h3>
          <div style={{display:'grid', gridTemplateColumns:'repeat(4, 1fr)', gap:10, marginBottom:18}}>
            {[
              { gl:'◐', t:'Map',        d:'Layers, style, popups, interactions' },
              { gl:'▤', t:'Dashboard',  d:'Charts, tables, filters, narrative' },
              { gl:'⊟', t:'Report',     d:'Long-form pages with maps + charts' },
              { gl:'☱', t:'Form',       d:'Survey-style field data capture' },
              { gl:'❒', t:'App',        d:'Multi-page workflow surface' },
              { gl:'◇', t:'Query',      d:'SQL/filter/spatial predicates' },
              { gl:'∑', t:'Analysis',   d:'Spatial/statistical analysis jobs' },
              { gl:'⇋', t:'Workflow',   d:'ETL pipelines, GP services' },
            ].map(c => (
              <div key={c.t} className="card" style={{padding:'10px 12px', cursor:'pointer'}}>
                <div className="row" style={{gap:8, marginBottom:4}}>
                  <span style={{fontSize:18, color:'#666'}}>{c.gl}</span>
                  <b style={{fontSize:12}}>{c.t}</b>
                </div>
                <div className="muted" style={{fontSize:10.5, lineHeight:1.5}}>{c.d}</div>
              </div>
            ))}
          </div>

          {/* recent projects */}
          <div className="row" style={{marginBottom:8}}>
            <h3 style={{margin:0, font:'600 13px var(--ui)'}}>Recent projects</h3>
            <span className="muted" style={{fontSize:11, marginLeft:8}}>your last 14 days</span>
            <div style={{flex:1}}/>
            <Btn ghost sm>View all in Catalog →</Btn>
          </div>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Title</th><th>Type</th><th>Status</th><th>Published</th><th>Modified</th><th>Owner</th><th></th>
            </tr></thead>
            <tbody>
              <tr><td><b>Parcels heatmap (FY24)</b></td><td>◐ Map</td><td><Badge kind="ok">published v4</Badge></td><td>Public</td><td className="muted">2h</td><td className="mono">jamie</td><td><Btn sm>Open</Btn></td></tr>
              <tr><td><b>Q3 land use review</b></td><td>▤ Dashboard</td><td><Badge>draft</Badge></td><td className="muted">—</td><td className="muted">5h</td><td className="mono">jamie</td><td><Btn sm>Open</Btn></td></tr>
              <tr><td><b>Hydrant maintenance form</b></td><td>☱ Form</td><td><Badge kind="ok">published v2</Badge></td><td>Field team</td><td className="muted">1d</td><td className="mono">k.tan</td><td><Btn sm>Open</Btn></td></tr>
              <tr><td><b>Fire perimeter trend chart</b></td><td>▤ Dashboard</td><td><Badge kind="warn">validation</Badge></td><td className="muted">—</td><td className="muted">3d</td><td className="mono">jamie</td><td><Btn sm>Open</Btn></td></tr>
              <tr><td><b>Census tract aggregation</b></td><td>∑ Analysis</td><td><Badge kind="ok">job complete</Badge></td><td>Catalog</td><td className="muted">5d</td><td className="mono">a.lee</td><td><Btn sm>Open</Btn></td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

/* ---------- AI MAP — prompt + clarification + package preview ---------- */
function StudioMapAI() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Map','New from prompt']} area="studio" />
      <StudioSidebar active="map" />
      <div className="main" style={{display:'grid', gridTemplateColumns:'380px 1fr', overflow:'hidden'}}>

        {/* LEFT: Conversation */}
        <div style={{borderRight:'1px solid #e4e4e4', display:'flex', flexDirection:'column', background:'#fafafa'}}>
          <div style={{padding:'10px 14px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
            <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Conversation</div>
            <div style={{font:'600 12px var(--ui)'}}>Map from prompt</div>
          </div>

          <div style={{flex:1, overflow:'auto', padding:'12px 14px', display:'flex', flexDirection:'column', gap:12}}>
            {/* User msg 1 */}
            <div>
              <div className="muted" style={{fontSize:10}}>You · 2 min ago</div>
              <div style={{padding:'8px 10px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:5, marginTop:2, fontSize:11.5}}>
                Show parcels in California colored by use code, with a popup showing assessed value and last assessment date.
              </div>
            </div>

            {/* AI msg 1 — clarification */}
            <div>
              <div className="muted" style={{fontSize:10}}>Honua · just now</div>
              <div style={{padding:'8px 10px', background:'#fffae0', border:'1.2px solid var(--accent-deep)', borderRadius:5, marginTop:2, fontSize:11.5}}>
                <div style={{marginBottom:6}}>I'll use <span className="mono">parcels_2024</span> (1.28M features, MultiPolygon, EPSG:4326). It has <span className="mono">use_code</span>, <span className="mono">assessed_value</span>, and <span className="mono">last_assessment</span> fields.</div>
                <div style={{marginBottom:6}}><b>Two clarifications:</b></div>
                <div className="col" style={{gap:6}}>
                  <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4}}>
                    <div style={{fontSize:11, fontWeight:600}}>1. Use-code colours</div>
                    <div className="muted" style={{fontSize:10.5, marginBottom:4}}>12 distinct codes. Categorical palette?</div>
                    <div className="row" style={{gap:4, fontSize:10}}>
                      <label className="row" style={{gap:3}}><input type="radio" readOnly defaultChecked /> categorical (12 colours)</label>
                      <label className="row" style={{gap:3}}><input type="radio" readOnly /> grouped (5 super-categories)</label>
                    </div>
                  </div>
                  <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4}}>
                    <div style={{fontSize:11, fontWeight:600}}>2. Owner field?</div>
                    <div className="muted" style={{fontSize:10.5, marginBottom:4}}><span className="mono">owner_name</span> is flagged PII. Include in popup?</div>
                    <div className="row" style={{gap:4, fontSize:10}}>
                      <label className="row" style={{gap:3}}><input type="radio" readOnly /> include</label>
                      <label className="row" style={{gap:3}}><input type="radio" readOnly defaultChecked /> redact (recommended)</label>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            {/* User msg 2 — answer */}
            <div>
              <div className="muted" style={{fontSize:10}}>You · just now</div>
              <div style={{padding:'8px 10px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:5, marginTop:2, fontSize:11.5}}>
                Categorical, and redact owner.
              </div>
            </div>

            {/* AI msg 2 — package built */}
            <div>
              <div className="muted" style={{fontSize:10}}>Honua · just now</div>
              <div style={{padding:'8px 10px', background:'#ecf7f0', border:'1px solid #8fcfa6', borderRadius:5, marginTop:2, fontSize:11.5}}>
                <div style={{marginBottom:4}}>Map package <b>ready for preview</b> →</div>
                <div className="muted" style={{fontSize:10.5}}>4 layers · 1 popup template · 12-class categorical renderer · public-read access default. <a className="fs-override" style={{fontSize:10.5}}>view evidence</a></div>
              </div>
            </div>
          </div>

          {/* prompt input */}
          <div style={{padding:'8px 12px', borderTop:'1px solid #e4e4e4', background:'#fff'}}>
            <textarea readOnly className="inp" style={{height:40, padding:8, fontSize:11.5, resize:'none'}} placeholder="Refine, or ask a follow-up…" />
            <div className="row" style={{marginTop:6}}>
              <span className="muted" style={{fontSize:10}}>AI suggestions are advisory · evidence linked</span>
              <div style={{flex:1}}/>
              <Btn ghost sm>Attach data</Btn>
              <Btn kind="p" sm>Send ⌘↵</Btn>
            </div>
          </div>
        </div>

        {/* RIGHT: Package + preview */}
        <div style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
          {/* Header strip */}
          <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
            <span style={{fontSize:16, color:'#666'}}>◐</span>
            <input readOnly className="inp" style={{width:240, fontWeight:600}} value="parcels-use-map" />
            <Badge>draft</Badge>
            <Badge kind="ok">validation passed</Badge>
            <div style={{flex:1}}/>
            <Btn ghost>Save draft</Btn>
            <Btn>Open editor →</Btn>
            <Btn kind="p">Publish…</Btn>
          </div>

          {/* Package inspector + preview side-by-side */}
          <div style={{display:'grid', gridTemplateColumns:'320px 1fr', flex:1, overflow:'hidden'}}>
            {/* Package inspector */}
            <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
                <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Map package</div>
                <div style={{font:'600 11.5px var(--ui)'}}>parcels-use-map · v1 draft</div>
              </div>

              <div style={{padding:'10px 12px'}}>
                <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Data bindings · 1</div>
                <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, marginBottom:10}}>
                  <div className="row" style={{fontSize:11}}>
                    <span style={{color:'var(--pencil)'}}>◇</span>
                    <span style={{flex:1, fontWeight:600}}>parcels_2024</span>
                    <Badge kind="ok">bound</Badge>
                  </div>
                  <div className="muted" style={{fontSize:10}}>1.28M features · MultiPolygon · 4326</div>
                </div>

                <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Layers · 4</div>
                {[
                  { n:'parcels/fill', t:'fill · categorical use_code (12 classes)' },
                  { n:'parcels/outline', t:'line · 0.5px #555 @ z≥12' },
                  { n:'parcels/labels', t:'symbol · {parcel_id} @ z≥14' },
                  { n:'basemap/positron', t:'raster · standard' },
                ].map((l,i) => (
                  <div key={i} style={{padding:'4px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, marginBottom:4, fontSize:11}}>
                    <div className="mono" style={{fontSize:10.5, fontWeight:600}}>{l.n}</div>
                    <div className="muted" style={{fontSize:10}}>{l.t}</div>
                  </div>
                ))}

                <div className="muted" style={{fontSize:10.5, marginTop:10, marginBottom:4}}>Popup template</div>
                <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, fontSize:11}}>
                  <div className="mono" style={{fontSize:10}}>Title: Parcel {`{{parcel_id}}`}</div>
                  <div className="mono" style={{fontSize:10}}>Use · area · assessed · last assessed</div>
                  <div className="muted" style={{fontSize:10}}>owner_name redacted</div>
                </div>

                <div className="muted" style={{fontSize:10.5, marginTop:10, marginBottom:4}}>Initial extent</div>
                <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, fontFamily:'var(--mono)', fontSize:10}}>
                  -124.4, 32.5 → -114.1, 42.0
                </div>

                <div className="muted" style={{fontSize:10.5, marginTop:10, marginBottom:4}}>Validation</div>
                <div style={{padding:'6px 8px', background:'#ecf7f0', border:'1px solid #8fcfa6', borderRadius:4, fontSize:10.5}}>
                  ✓ data bindings exist<br/>✓ schema matches<br/>✓ access policy clean<br/>✓ all 12 use codes have colours
                </div>

                <details style={{marginTop:10}}>
                  <summary style={{fontSize:10.5, color:'var(--pencil)', cursor:'pointer'}}>View raw package JSON</summary>
                </details>
              </div>
            </div>

            {/* Preview */}
            <div style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', display:'flex', alignItems:'center', gap:8}}>
                <div style={{display:'inline-flex', border:'1px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:10.5}}>
                  <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Preview</div>
                  <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>Mobile</div>
                </div>
                <div style={{flex:1}}/>
                <span className="muted" style={{fontSize:10.5}}>1k sampled features</span>
              </div>
              <div style={{flex:1, padding:14}}>
                <MapPreview mode="layer" height={460} popup={true} scaleText="1:1,000,000" />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- MAP EDITOR ---------- */
function StudioMapEditor() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Map','parcels-use-map']} area="studio" />
      <StudioSidebar active="map" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>◐</span>
          <input readOnly className="inp" style={{width:240, fontWeight:600}} value="parcels-use-map" />
          <Badge>draft v1</Badge>
          <Badge kind="ok">validation passed</Badge>
          <div style={{flex:1}}/>
          <Btn ghost>↶ Undo</Btn>
          <Btn ghost>Save draft</Btn>
          <Btn ghost>← Back to chat</Btn>
          <Btn kind="p">Publish…</Btn>
        </div>

        <Tabs items={[
          { k:'design', t:'Design' },
          { k:'data', t:'Data bindings', ct: 1 },
          { k:'interactions', t:'Interactions' },
          { k:'access', t:'Access' },
          { k:'package', t:'Package · raw' },
        ]} active="design" />

        <div style={{display:'grid', gridTemplateColumns:'260px 1fr 280px', flex:1, overflow:'hidden'}}>
          {/* LEFT: layers list */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Layers</h3>
              <div style={{flex:1}}/>
              <Btn ghost sm>+ Layer</Btn>
            </div>
            {[
              { n:'parcels/labels', t:'symbol', on:true },
              { n:'parcels/outline', t:'line', on:true },
              { n:'parcels/fill', t:'fill', on:true, sel:true },
              { n:'basemap/positron', t:'raster', on:true },
            ].map(l => (
              <div key={l.n} className="row" style={{
                padding:'5px 12px', borderBottom:'1px solid #f1f1f1',
                background: l.sel ? '#fffae0' : 'transparent',
                borderLeft: l.sel ? '3px solid var(--ink)' : '3px solid transparent',
                cursor:'pointer', fontSize:11
              }}>
                <input type="checkbox" readOnly defaultChecked={l.on} />
                <span className="mono" style={{flex:1, fontWeight: l.sel ? 600 : 400}}>{l.n}</span>
                <span className="muted" style={{fontSize:9.5}}>{l.t}</span>
              </div>
            ))}
            <div style={{padding:'8px 12px', borderTop:'1px dashed #d8d8d8'}}>
              <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Filter</div>
              <input readOnly className="inp" style={{fontSize:10.5}} value="use_code IN ('R-1','C-2')" placeholder="WHERE clause…" />
            </div>
          </div>

          {/* MIDDLE: live preview */}
          <div style={{padding:14, background:'#fff', overflow:'auto', display:'flex', flexDirection:'column', gap:10}}>
            <MapPreview mode="layer" height={420} popup={true} scaleText="1:8,000" />
            <div className="row" style={{fontSize:10.5}}>
              <span className="muted">Edits apply live · 1k sampled features · zoom 14</span>
              <div style={{flex:1}}/>
              <FiltChip on>labels</FiltChip>
              <FiltChip on>popups</FiltChip>
              <FiltChip>basemap: positron</FiltChip>
            </div>
          </div>

          {/* RIGHT: inspector for selected layer */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>parcels/fill · paint</h3>
            </div>
            <div style={{padding:'10px 12px', fontSize:11}}>
              <div className="muted" style={{fontSize:10.5, marginBottom:3}}>fill-color · data-driven</div>
              <div style={{padding:6, border:'1px solid #d0d0d0', borderRadius:4, background:'#fff', marginBottom:10}}>
                <div className="muted" style={{fontSize:9.5, marginBottom:4}}>categorical on use_code · 12 classes</div>
                {[
                  ['#d9a23a','R-1 · residential'],
                  ['#b56b1c','C-2 · commercial'],
                  ['#612d0a','I-1 · industrial'],
                  ['#7ca7b3','W-1 · wetland'],
                  ['#4a8b6f','A-1 · agriculture'],
                  ['#888','+ 7 more'],
                ].map(([c,l],i) => (
                  <div key={i} className="row" style={{gap:6, padding:'2px 0', fontSize:10}}>
                    <span style={{width:12, height:12, background:c, border:'1px solid #999', display:'inline-block'}}/>
                    <span>{l}</span>
                  </div>
                ))}
              </div>

              <div className="muted" style={{fontSize:10.5, marginBottom:3}}>fill-opacity</div>
              <div className="row" style={{gap:6, marginBottom:10}}>
                <input type="range" defaultValue={85} style={{flex:1}} />
                <span className="mono" style={{fontSize:10}}>0.85</span>
              </div>

              <div className="muted" style={{fontSize:10.5, marginBottom:3}}>zoom range</div>
              <div className="row" style={{gap:6, marginBottom:10, fontSize:10.5}}>
                <Inp mono value="10" />
                <span className="muted">→</span>
                <Inp mono value="22" />
              </div>

              <Btn ghost sm>Open in Maputnik ↗</Btn>
            </div>
            <div style={{padding:'10px 12px', borderTop:'1px dashed #d8d8d8'}}>
              <h3 style={{margin:0, fontSize:11.5, marginBottom:4}}>Popup template</h3>
              <Btn ghost sm>Edit template</Btn>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- PUBLISH REVIEW ---------- */
function StudioMapPublish() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Studio','Map','parcels-use-map','Publish']} area="studio" />
      <div className="wiz">
        <Stepper steps={['Validate','Dependencies','Visibility','Embed','Rollback','Confirm']} on={2} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{fontSize:16, color:'#666'}}>◐</span>
          <b>parcels-use-map</b>
          <Badge>draft v1</Badge>
          <span className="muted">→ publish as content item · public read · embeddable</span>
          <div style={{flex:1}}/>
          <Badge kind="ok">validation passed</Badge>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, overflow:'auto'}}>
          <div className="col">
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Visibility</h2>
            <div className="muted" style={{fontSize:11.5, marginBottom:14}}>Who can see this map after publish.</div>

            <div className="col" style={{gap:6}}>
              {[
                { v:'public', l:'Public', d:'Anyone with the link · indexed by search engines · default for public-read data', on:true },
                { v:'org', l:'Organisation', d:'Anyone signed in to this workspace' },
                { v:'team', l:'Specific teams', d:'Pick teams below' },
                { v:'private', l:'Private', d:'Only you and admins' },
              ].map(o => (
                <label key={o.v} className="row" style={{
                  border: o.on ? '1.5px solid var(--ink)' : '1px solid #d8d8d8',
                  background: o.on ? '#fffae0' : '#fff',
                  borderRadius:5, padding:'8px 10px', gap:8, cursor:'pointer'
                }}>
                  <input type="radio" readOnly defaultChecked={o.on} />
                  <div style={{flex:1}}>
                    <div style={{fontSize:11.5, fontWeight:600}}>{o.l}</div>
                    <div className="muted" style={{fontSize:10.5}}>{o.d}</div>
                  </div>
                </label>
              ))}
            </div>

            <div className="card" style={{marginTop:12}}>
              <h3>What public users will see</h3>
              <div className="muted" style={{fontSize:11, marginBottom:6}}>Preview rendered as if signed-out. owner_name field is auto-redacted.</div>
              <MapPreview mode="layer" height={220} popup={true} scaleText="1:8,000" />
            </div>

            <div className="card">
              <h3>Route</h3>
              <Field label="Public URL" hint="auto-derived from content title">
                <div style={{height:26, border:'1px dashed #c0c0c0', borderRadius:4, background:'#fafafa', padding:'0 8px', display:'flex',alignItems:'center', font:'11px var(--mono)', color:'#666'}}>
                  honua.example.gov/m/parcels-use-map
                  <span style={{flex:1}}/><span style={{color:'#bbb',fontSize:9.5}}>auto</span>
                </div>
              </Field>
            </div>
          </div>

          <div className="col">
            <div className="card">
              <h3>Dependencies</h3>
              <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
                <tbody>
                  <tr><td>parcels_2024</td><td>data resource</td><td><Badge kind="ok">public</Badge></td></tr>
                  <tr><td>basemap/positron</td><td>basemap</td><td><Badge kind="ok">licensed</Badge></td></tr>
                  <tr><td>style:parcels-use</td><td>map style</td><td><Badge kind="ok">canonical v1</Badge></td></tr>
                </tbody>
              </table>
            </div>

            <Callout kind="info">
              <b>Publish creates a content item.</b> v1 becomes the published version. Future edits start a v2 draft. Rollback is one click from item history.
            </Callout>

            <div className="card">
              <h3>Sharing policy</h3>
              <dl className="kv">
                <dt>Visibility</dt><dd>Public</dd>
                <dt>Embed</dt><dd>allowed · any origin</dd>
                <dt>Export</dt><dd>GeoJSON, PNG, PDF</dd>
                <dt>Open data page</dt><dd>opt-in (next step)</dd>
                <dt>License</dt><dd>CC-BY 4.0 (from resource)</dd>
              </dl>
            </div>

            <Ann red>publish is a governed operation. produces an audit event + content version.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Dependencies</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Embed →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- AI DASHBOARD ---------- */
function StudioDashboardAI() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Dashboard','New from prompt']} area="studio" />
      <StudioSidebar active="dashboard" />
      <div className="main" style={{display:'grid', gridTemplateColumns:'380px 1fr', overflow:'hidden'}}>

        {/* Conversation */}
        <div style={{borderRight:'1px solid #e4e4e4', display:'flex', flexDirection:'column', background:'#fafafa'}}>
          <div style={{padding:'10px 14px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
            <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Conversation</div>
            <div style={{font:'600 12px var(--ui)'}}>Dashboard from prompt</div>
          </div>

          <div style={{flex:1, overflow:'auto', padding:'12px 14px', display:'flex', flexDirection:'column', gap:12}}>
            <div>
              <div className="muted" style={{fontSize:10}}>You</div>
              <div style={{padding:'8px 10px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:5, marginTop:2, fontSize:11.5}}>
                Build a Q3 land use dashboard — counts by use code over time, map of recent assessments, and a totals table.
              </div>
            </div>
            <div>
              <div className="muted" style={{fontSize:10}}>Honua · just now</div>
              <div style={{padding:'8px 10px', background:'#ecf7f0', border:'1px solid #8fcfa6', borderRadius:5, marginTop:2, fontSize:11.5}}>
                <div style={{marginBottom:4}}>Built dashboard with 4 panels:</div>
                <ul style={{margin:'0 0 0 16px', padding:0, fontSize:11, lineHeight:1.6}}>
                  <li>Stacked area chart · use_code over last_assessment (12 series)</li>
                  <li>Map · parcels assessed in Q3 2024</li>
                  <li>Totals table · count + sum(assessed_value) by use_code</li>
                  <li>KPI strip · total parcels, total value, avg lot size</li>
                </ul>
                <div className="muted" style={{fontSize:10, marginTop:4}}>charts use Vega-Lite · <a className="fs-override" style={{fontSize:10}}>view evidence</a></div>
              </div>
            </div>
          </div>

          <div style={{padding:'8px 12px', borderTop:'1px solid #e4e4e4', background:'#fff'}}>
            <textarea readOnly className="inp" style={{height:40, padding:8, fontSize:11.5, resize:'none'}} placeholder="Refine…" />
            <div className="row" style={{marginTop:6}}>
              <span className="muted" style={{fontSize:10}}>Try: "Add a filter for county" · "Group residential codes"</span>
              <div style={{flex:1}}/>
              <Btn kind="p" sm>Send ⌘↵</Btn>
            </div>
          </div>
        </div>

        {/* Preview */}
        <div style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
          <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
            <span style={{fontSize:16}}>▤</span>
            <input readOnly className="inp" style={{width:240, fontWeight:600}} value="q3-land-use" />
            <Badge>draft</Badge>
            <Badge kind="ok">validation passed</Badge>
            <div style={{flex:1}}/>
            <Btn>Open editor →</Btn>
            <Btn kind="p">Publish…</Btn>
          </div>

          <div style={{flex:1, overflow:'auto', padding:'14px 18px', background:'#fafafa'}}>
            {/* KPI strip */}
            <div style={{display:'grid', gridTemplateColumns:'repeat(4, 1fr)', gap:10, marginBottom:14}}>
              {[
                ['Total parcels','1.28M','+2.1% vs Q2'],
                ['Total assessed','$248B','+4.4%'],
                ['Avg lot size','2,148 m²','—'],
                ['New assessments','82,401','Q3'],
              ].map((k,i) => (
                <div key={i} className="card" style={{padding:'8px 10px'}}>
                  <div className="muted" style={{fontSize:9.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>{k[0]}</div>
                  <div style={{font:'600 18px var(--ui)'}}>{k[1]}</div>
                  <div className="muted" style={{fontSize:10}}>{k[2]}</div>
                </div>
              ))}
            </div>

            {/* Chart + map */}
            <div style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:10, marginBottom:14}}>
              <div className="card" style={{padding:'10px 12px'}}>
                <div className="row" style={{marginBottom:6}}>
                  <b style={{fontSize:11.5}}>Assessments over time · by use code</b>
                  <span className="tag" style={{marginLeft:6}}>Vega-Lite</span>
                  <div style={{flex:1}}/>
                  <Btn ghost sm>Edit chart</Btn>
                </div>
                {/* Faked stacked area chart */}
                <svg viewBox="0 0 600 200" style={{width:'100%', height:200, display:'block'}}>
                  <defs>
                    <pattern id="dashgrid" width="60" height="40" patternUnits="userSpaceOnUse">
                      <path d="M 60 0 L 0 0 0 40" fill="none" stroke="#eee" strokeWidth="1"/>
                    </pattern>
                  </defs>
                  <rect width="600" height="200" fill="url(#dashgrid)"/>
                  {[
                    ['#612d0a', 'M 0 180 L 60 175 L 120 168 L 180 160 L 240 155 L 300 150 L 360 145 L 420 138 L 480 132 L 540 128 L 600 122 L 600 200 L 0 200 Z'],
                    ['#b56b1c', 'M 0 160 L 60 155 L 120 148 L 180 140 L 240 132 L 300 128 L 360 122 L 420 118 L 480 112 L 540 108 L 600 100 L 600 180 L 0 180 Z'],
                    ['#d9a23a', 'M 0 130 L 60 122 L 120 115 L 180 108 L 240 100 L 300 92 L 360 86 L 420 80 L 480 74 L 540 68 L 600 60 L 600 160 L 0 160 Z'],
                    ['#ead78a', 'M 0 90 L 60 84 L 120 76 L 180 68 L 240 60 L 300 54 L 360 48 L 420 42 L 480 36 L 540 30 L 600 24 L 600 130 L 0 130 Z'],
                  ].map(([c,p],i) => <path key={i} d={p} fill={c} opacity="0.85" />)}
                  {/* Axis */}
                  <line x1="0" y1="200" x2="600" y2="200" stroke="#888" strokeWidth="1"/>
                  <line x1="0" y1="0" x2="0" y2="200" stroke="#888" strokeWidth="1"/>
                  <g fontFamily="Inter" fontSize="9" fill="#888">
                    {['Jan','Mar','May','Jul','Sep','Nov'].map((m,i) => (
                      <text key={m} x={i*100 + 5} y="194">{m}</text>
                    ))}
                  </g>
                </svg>
                {/* legend */}
                <div className="row" style={{gap:8, fontSize:10, marginTop:4, flexWrap:'wrap'}}>
                  {[['#ead78a','R-1 residential'],['#d9a23a','R-2 multifamily'],['#b56b1c','C-2 commercial'],['#612d0a','I-1 industrial'],['#888','+ 8 more']].map(([c,l]) => (
                    <span key={l} className="row" style={{gap:3}}>
                      <span style={{width:10,height:10,background:c,border:'1px solid #999'}} /><span>{l}</span>
                    </span>
                  ))}
                </div>
              </div>

              <div className="card" style={{padding:'10px 12px'}}>
                <div className="row" style={{marginBottom:6}}>
                  <b style={{fontSize:11.5}}>Q3 assessments map</b>
                  <div style={{flex:1}}/>
                  <Btn ghost sm>Edit</Btn>
                </div>
                <MapPreview mode="layer" height={200} popup={false} scaleText="1:80,000" />
              </div>
            </div>

            {/* Totals table */}
            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center'}}>
                <b style={{fontSize:11.5}}>Totals by use code</b>
                <div style={{flex:1}}/>
                <FiltChip on>filter: 2024 Q3</FiltChip>
                <Btn ghost sm>Edit</Btn>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>Use code</th><th className="num">Parcels</th><th className="num">Total assessed</th><th className="num">Avg lot</th><th>Δ vs Q2</th></tr></thead>
                <tbody>
                  <tr><td><b>R-1</b> residential</td><td className="num mono">421,008</td><td className="num mono">$84.2B</td><td className="num mono">1,820</td><td><Badge kind="ok">+2.1%</Badge></td></tr>
                  <tr><td><b>R-2</b> multifamily</td><td className="num mono">88,221</td><td className="num mono">$32.7B</td><td className="num mono">2,420</td><td><Badge kind="ok">+1.4%</Badge></td></tr>
                  <tr><td><b>C-2</b> commercial</td><td className="num mono">42,108</td><td className="num mono">$48.1B</td><td className="num mono">4,820</td><td><Badge kind="ok">+5.2%</Badge></td></tr>
                  <tr><td><b>I-1</b> industrial</td><td className="num mono">12,408</td><td className="num mono">$28.9B</td><td className="num mono">9,200</td><td><Badge kind="warn">-1.1%</Badge></td></tr>
                  <tr><td colSpan="5" className="muted" style={{textAlign:'center',padding:6, fontSize:10.5}}>+ 8 more</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StudioDashboardEditor() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Dashboard','q3-land-use','Edit']} area="studio" />
      <StudioSidebar active="dashboard" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>▤</span>
          <input readOnly className="inp" style={{width:240, fontWeight:600}} value="q3-land-use" />
          <Badge>draft v1</Badge>
          <div style={{display:'inline-flex', border:'1px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:10.5, marginLeft:6}}>
            <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Desktop</div>
            <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Tablet</div>
            <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>Mobile</div>
          </div>
          <div style={{flex:1}}/>
          <Btn ghost>Save draft</Btn>
          <Btn kind="p">Publish…</Btn>
        </div>

        <div style={{display:'grid', gridTemplateColumns:'220px 1fr 320px', flex:1, overflow:'hidden'}}>
          {/* LEFT: panel list */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Panels</h3>
              <div style={{flex:1}}/>
              <Btn ghost sm>+ Panel</Btn>
            </div>
            {[
              { ico:'#', n:'KPI strip', t:'4 KPIs' },
              { ico:'∼', n:'Assessments over time', t:'Vega-Lite stacked area', sel:true },
              { ico:'◐', n:'Q3 assessments map', t:'Map · parcels filtered' },
              { ico:'▦', n:'Totals by use code', t:'Table · 12 rows' },
            ].map((p,i) => (
              <div key={i} className="row" style={{
                padding:'6px 12px', borderBottom:'1px solid #f1f1f1',
                background: p.sel ? '#fffae0' : 'transparent',
                borderLeft: p.sel ? '3px solid var(--ink)' : '3px solid transparent',
                cursor:'pointer', fontSize:11
              }}>
                <span style={{width:14,textAlign:'center',color:'#666'}}>{p.ico}</span>
                <div style={{flex:1, minWidth:0}}>
                  <div style={{fontWeight: p.sel ? 600 : 500}}>{p.n}</div>
                  <div className="muted" style={{fontSize:10}}>{p.t}</div>
                </div>
              </div>
            ))}
            <div style={{padding:'10px 12px', borderTop:'1px dashed #d8d8d8'}}>
              <h3 style={{margin:0, fontSize:11.5, marginBottom:6}}>Filters</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <FiltChip on x>year: 2024</FiltChip>
                <FiltChip on x>quarter: Q3</FiltChip>
                <Btn ghost sm>+ Filter</Btn>
              </div>
            </div>
          </div>

          {/* MIDDLE: canvas */}
          <div style={{padding:14, overflow:'auto', background:'#fafafa'}}>
            {/* mini KPIs */}
            <div style={{display:'grid', gridTemplateColumns:'repeat(4, 1fr)', gap:10, marginBottom:10}}>
              {[1,2,3,4].map(i => (
                <div key={i} style={{height:62, background:'#fff', border:'1px solid #e4e4e4', borderRadius:4}}/>
              ))}
            </div>
            <div style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:10, marginBottom:10}}>
              <div style={{
                border:'2px solid var(--ink)', borderRadius:4, padding:8, background:'#fff'
              }}>
                <div className="muted" style={{fontSize:10}}>Assessments over time · Vega-Lite</div>
                <div style={{height:200, background:'#fff', display:'grid', placeItems:'center', color:'#aaa', fontSize:11}}>chart preview</div>
              </div>
              <div style={{height:220, background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, padding:8}}>
                <div className="muted" style={{fontSize:10}}>Q3 map</div>
              </div>
            </div>
            <div style={{height:200, background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, padding:8}}>
              <div className="muted" style={{fontSize:10}}>Totals table</div>
            </div>
          </div>

          {/* RIGHT: chart inspector */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Assessments over time</h3>
              <div className="muted" style={{fontSize:10}}>Vega-Lite stacked area</div>
            </div>
            <div style={{padding:'10px 12px'}}>
              <FieldGroup title="Data binding" scope="resource" count="1 binding">
                <FieldRow state="input" label="Source"><Sel value="parcels_2024" /></FieldRow>
                <FieldRow state="input" label="Filter" hint="dashboard filters override"><Inp mono value="EXTRACT(YEAR FROM last_assessment) = 2024" /></FieldRow>
              </FieldGroup>
              <FieldGroup title="Encoding">
                <FieldRow state="input" label="x · time"><Sel value="last_assessment · month" /></FieldRow>
                <FieldRow state="input" label="y · count"><Sel value="count(*)" /></FieldRow>
                <FieldRow state="input" label="color · series"><Sel value="use_code · categorical" /></FieldRow>
                <FieldRow state="input" label="stack"><Sel value="stacked" /></FieldRow>
              </FieldGroup>
              <details style={{margin:'8px 12px 0'}}>
                <summary style={{fontSize:10.5, color:'var(--pencil)', cursor:'pointer'}}>View raw Vega-Lite spec</summary>
                <pre className="mono" style={{margin:'4px 0 0', padding:8, background:'#0e0e0e', color:'#d8d8d8', borderRadius:4, fontSize:9.5, lineHeight:1.5, whiteSpace:'pre-wrap'}}>
{`{
  "$schema": "https://vega.github.io/schema/vega-lite/v5.json",
  "data": { "name": "parcels_2024" },
  "mark": "area",
  "encoding": {
    "x": {"field":"last_assessment","timeUnit":"month"},
    "y": {"aggregate":"count","stack":true},
    "color": {"field":"use_code","type":"nominal"}
  }
}`}
                </pre>
              </details>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StudioDashboardPublish() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Studio','Dashboard','q3-land-use','Publish']} area="studio" />
      <div className="wiz">
        <Stepper steps={['Validate','Dependencies','Visibility','Embed','Rollback','Confirm']} on={5} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{fontSize:16}}>▤</span>
          <b>q3-land-use</b>
          <Badge>v1 draft</Badge>
          <span className="muted">→ dashboard content item</span>
          <div style={{flex:1}}/>
          <Badge kind="ok">all checks passed</Badge>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24}}>
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Final confirmation</h2>
            <div className="muted" style={{fontSize:11.5, marginBottom:14}}>Publish creates content version v1 and an operational event. Future edits start v2 drafts. Rollback is available from item history.</div>

            <div className="card">
              <h3>Publishing as</h3>
              <dl className="kv">
                <dt>Content type</dt><dd>Dashboard</dd>
                <dt>Title</dt><dd>Q3 land use review</dd>
                <dt>Identifier</dt><dd className="mono">q3-land-use</dd>
                <dt>Version</dt><dd className="mono">v1</dd>
                <dt>Owner</dt><dd>jamie</dd>
                <dt>Workspace</dt><dd>Public Works</dd>
                <dt>Environment</dt><dd>dev <span className="muted">(promote to prod via Operate → Environments)</span></dd>
              </dl>
            </div>

            <div className="card">
              <h3>Settings summary</h3>
              <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
                <tbody>
                  <tr><td className="muted">Visibility</td><td>Public</td></tr>
                  <tr><td className="muted">Embed</td><td>allowed · any origin</td></tr>
                  <tr><td className="muted">Open data page</td><td>opt-in · skipped</td></tr>
                  <tr><td className="muted">Rollback policy</td><td>retain v1 forever · auto on first failure</td></tr>
                  <tr><td className="muted">Dependencies</td><td>parcels_2024 (data) · vega-lite 5.x (chart engine)</td></tr>
                </tbody>
              </table>
            </div>

            <Callout kind="info">
              <b>Audit trail.</b> Publishing emits an operational event; an evidence link is attached to the content version. Operate → Event Viewer surfaces this immediately.
            </Callout>
          </div>

          <div className="col">
            <div className="card" style={{background:'#ecf7f0', borderLeft:'3px solid var(--ok)'}}>
              <h3>Ready to publish</h3>
              <ul style={{margin:'4px 0 0 16px', padding:0, fontSize:11, lineHeight:1.6}}>
                <li>Validation passed</li>
                <li>Dependencies resolved</li>
                <li>Visibility set</li>
                <li>Embed policy set</li>
                <li>Rollback plan confirmed</li>
              </ul>
            </div>

            <div className="card">
              <h3>What gets created</h3>
              <ol style={{margin:'0 0 0 16px', padding:0, fontSize:11, lineHeight:1.65}}>
                <li>Content item <span className="mono">q3-land-use</span></li>
                <li>Content version <span className="mono">v1</span></li>
                <li>Publication record (public, embeddable)</li>
                <li>Operational event</li>
                <li>Catalog entry under Public Works</li>
              </ol>
            </div>

            <Ann red>this is a governed operation. emits audit + creates content version.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Rollback</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="a">Publish dashboard</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, {
  StudioHome, StudioMapAI, StudioMapEditor, StudioMapPublish,
  StudioDashboardAI, StudioDashboardEditor, StudioDashboardPublish,
});
