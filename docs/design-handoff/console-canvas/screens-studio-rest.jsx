// Studio · remaining 4 content types: Report, App, Query, Analysis
function StudioReportEditor() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Report','q3-public-works-overview','Edit']} area="studio" />
      <StudioSidebar active="report" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>⊟</span>
          <input readOnly className="inp" style={{width:280, fontWeight:600}} value="q3-public-works-overview" />
          <Badge>draft v2</Badge>
          <span className="muted" style={{fontSize:11}}>12 pages · 4 embedded items · last autosave 2m</span>
          <div style={{flex:1}}/>
          <Btn ghost>Print preview</Btn>
          <Btn ghost>Export PDF</Btn>
          <Btn kind="p">Publish…</Btn>
        </div>

        <div style={{display:'grid', gridTemplateColumns:'200px 1fr 280px', flex:1, overflow:'hidden'}}>
          {/* outline */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Outline</h3>
            </div>
            {[
              { n:'1. Executive summary', d:1, sel:false },
              { n:'2. Parcels overview', d:1, sel:true },
              { n:'  Parcels heatmap', d:2, kind:'map' },
              { n:'  Use-code table', d:2, kind:'table' },
              { n:'3. Roads & infrastructure', d:1 },
              { n:'  Roads condition map', d:2, kind:'map' },
              { n:'4. Q3 fire perimeters', d:1 },
              { n:'  Fire trend chart', d:2, kind:'chart' },
              { n:'5. Hydrant inspections', d:1 },
              { n:'  Inspection KPIs', d:2, kind:'kpi' },
              { n:'6. Conclusions', d:1 },
              { n:'7. Appendix', d:1 },
            ].map((s,i) => (
              <div key={i} className="row" style={{
                padding:'4px 12px', paddingLeft: 8 + s.d * 12,
                borderBottom:'1px solid #f1f1f1',
                background: s.sel ? '#fffae0' : 'transparent',
                fontSize:11, cursor:'pointer'
              }}>
                <span style={{flex:1, fontWeight: s.sel ? 600 : 400, color: s.d > 1 ? '#666' : 'var(--ink)'}}>{s.n}</span>
                {s.kind && <span className="tag" style={{fontSize:9}}>{s.kind}</span>}
              </div>
            ))}
          </div>

          {/* page */}
          <div style={{padding:'24px 28px', overflow:'auto', background:'#fafafa'}}>
            <div style={{maxWidth:760, margin:'0 auto', background:'#fff', border:'1px solid #d8d8d8', borderRadius:4, padding:'40px 52px', boxShadow:'0 1px 3px rgba(0,0,0,0.06)'}}>
              <div className="muted" style={{fontSize:10, textTransform:'uppercase', letterSpacing:'0.08em', marginBottom:4}}>section 2</div>
              <h1 style={{font:'600 22px var(--ui)', margin:'0 0 14px'}}>Parcels overview</h1>
              <p style={{fontSize:12.5, lineHeight:1.7, color:'#333', marginBottom:14}}>
                Statewide tax parcels for fiscal year 2024 saw a <b>2.1% net increase</b> in count vs Q2, driven primarily by new R-1 residential subdivisions in three counties. Average lot size remained flat at 2,148 m², while assessed values climbed 4.4% on the strength of commercial and industrial reassessments.
              </p>

              {/* embedded map */}
              <div style={{border:'2px dashed var(--accent-deep)', borderRadius:6, padding:6, marginBottom:14}}>
                <div className="row" style={{marginBottom:4, fontSize:10}}>
                  <span className="tag">embedded map</span>
                  <span className="muted" style={{marginLeft:6}}>◐ Parcels heatmap (FY24) · v4 · live</span>
                  <div style={{flex:1}}/>
                  <a className="fs-override" style={{fontSize:10}}>open in Studio ↗</a>
                </div>
                <MapPreview mode="layer" height={200} popup={false} scaleText="1:1,000,000" />
                <div className="muted" style={{fontSize:10, marginTop:4, fontStyle:'italic'}}>Figure 1. Statewide parcels coloured by use code, FY 2024.</div>
              </div>

              <p style={{fontSize:12.5, lineHeight:1.7, color:'#333', marginBottom:10}}>
                Residential codes (R-1, R-2) account for 71% of total parcels and 47% of total assessed value, reflecting the long-standing pattern across the state. Commercial parcels saw the largest valuation increase quarter-over-quarter (<b>+5.2%</b>).
              </p>

              {/* embedded table */}
              <div style={{border:'1px solid #e4e4e4', borderRadius:4, marginBottom:14}}>
                <div style={{padding:'6px 10px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', fontSize:10.5, color:'#666'}}>Table 1. Totals by use code, FY 2024.</div>
                <table className="tbl tbl--cmpt" style={{fontSize:11}}>
                  <thead><tr><th>Use</th><th className="num">Parcels</th><th className="num">Assessed (USD B)</th><th>Δ Q2</th></tr></thead>
                  <tbody>
                    <tr><td>R-1 residential</td><td className="num mono">421,008</td><td className="num mono">84.2</td><td><Badge kind="ok">+2.1%</Badge></td></tr>
                    <tr><td>R-2 multifamily</td><td className="num mono">88,221</td><td className="num mono">32.7</td><td><Badge kind="ok">+1.4%</Badge></td></tr>
                    <tr><td>C-2 commercial</td><td className="num mono">42,108</td><td className="num mono">48.1</td><td><Badge kind="ok">+5.2%</Badge></td></tr>
                    <tr><td>I-1 industrial</td><td className="num mono">12,408</td><td className="num mono">28.9</td><td><Badge kind="warn">-1.1%</Badge></td></tr>
                  </tbody>
                </table>
              </div>

              <h2 style={{font:'600 14px var(--ui)', margin:'18px 0 6px'}}>Notable shifts</h2>
              <p style={{fontSize:12.5, lineHeight:1.7, color:'#333'}}>
                The industrial category contracted slightly as a result of three multi-parcel redevelopments in the Bay region, where I-1 parcels were converted to mixed-use C-2 designations.
              </p>
            </div>
          </div>

          {/* inspector for selected embedded map */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Embedded · Map</h3>
              <div className="muted" style={{fontSize:10}}>Parcels heatmap (FY24)</div>
            </div>
            <div style={{padding:'10px 12px'}}>
              <FieldGroup title="Source binding" scope="resource" count="3 downstream items">
                <FieldRow state="input" label="Content item"><Sel value="◐ parcels-heatmap" /></FieldRow>
                <FieldRow state="input" label="Version pin">
                  <div className="col" style={{gap:3, fontSize:11}}>
                    <label className="row" style={{gap:6}}><input type="radio" readOnly /> always latest</label>
                    <label className="row" style={{gap:6}}><input type="radio" readOnly defaultChecked /> pin to v4 (recommended for archived reports)</label>
                  </div>
                </FieldRow>
                <FieldRow state="calculated" label="Resolved version" value="parcels-heatmap@v4" derivedFrom="pin" />
              </FieldGroup>
              <FieldGroup title="Presentation">
                <FieldRow state="input" label="Caption"><Inp value="Figure 1. Statewide parcels coloured by use code, FY 2024." /></FieldRow>
                <FieldRow state="input" label="Show legend"><label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked /><span>yes</span></label></FieldRow>
                <FieldRow state="input" label="Print mode"><Sel value="static snapshot · PDF-safe" /></FieldRow>
              </FieldGroup>
              <Callout kind="info" style={{margin:'8px 12px'}}>
                Pinning to v4 means newer publishes of the map won't change this report. Use "always latest" for live dashboards, pinned versions for archival reports.
              </Callout>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StudioAppEditor() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','App','wetlands-inspector','Edit']} area="studio" />
      <StudioSidebar active="app" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>❒</span>
          <input readOnly className="inp" style={{width:240, fontWeight:600}} value="wetlands-inspector" />
          <Badge>draft v3</Badge>
          <span className="muted" style={{fontSize:11}}>4 pages · 12 components · public read</span>
          <div style={{flex:1}}/>
          <Btn ghost>Live preview ↗</Btn>
          <Btn kind="p">Publish…</Btn>
        </div>

        <div style={{display:'grid', gridTemplateColumns:'200px 1fr 280px', flex:1, overflow:'hidden'}}>
          {/* page tree + components */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Pages</h3>
              <div style={{flex:1}}/>
              <Btn ghost sm>+ Page</Btn>
            </div>
            {[
              { n:'Home', icon:'🏠' },
              { n:'Map · Wetlands', icon:'◐', sel:true },
              { n:'Inspect a site', icon:'☱' },
              { n:'About the data', icon:'⊟' },
            ].map(p => (
              <div key={p.n} className="row" style={{
                padding:'6px 12px', borderBottom:'1px solid #f1f1f1',
                background: p.sel ? '#fffae0' : 'transparent',
                borderLeft: p.sel ? '3px solid var(--ink)' : '3px solid transparent',
                fontSize:11, cursor:'pointer'
              }}>
                <span style={{width:16, textAlign:'center'}}>{p.icon}</span>
                <span style={{flex:1, fontWeight: p.sel ? 600 : 400}}>{p.n}</span>
              </div>
            ))}
            <div style={{padding:'10px 12px', borderTop:'1px dashed #d8d8d8'}}>
              <h3 style={{margin:0, fontSize:11.5, marginBottom:6}}>Components</h3>
              <div className="col" style={{gap:4}}>
                {['Map', 'Sidebar list', 'Detail panel', 'Filter chips', 'Search', 'Header', 'Footer'].map(c => (
                  <div key={c} className="row" style={{padding:'3px 6px', border:'1px solid #e4e4e4', borderRadius:3, background:'#fff', fontSize:10.5, cursor:'grab'}}>
                    <span style={{flex:1}}>{c}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* page canvas — desktop frame */}
          <div style={{padding:18, overflow:'auto', background:'#fafafa', display:'flex', justifyContent:'center'}}>
            <div style={{width:'100%', maxWidth:760, background:'#fff', border:'1px solid #d8d8d8', borderRadius:6, overflow:'hidden'}}>
              <div style={{padding:'8px 12px', background:'#fafafa', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:8}}>
                <div style={{display:'flex', gap:4}}><span style={{width:8,height:8,borderRadius:'50%',background:'#ff5f57'}}/><span style={{width:8,height:8,borderRadius:'50%',background:'#ffbd2e'}}/><span style={{width:8,height:8,borderRadius:'50%',background:'#28c940'}}/></div>
                <div style={{flex:1, fontSize:10, color:'#888', textAlign:'center'}}>honua.example.gov/a/wetlands-inspector/map</div>
              </div>
              <div style={{padding:'10px 14px', borderBottom:'1px solid #e4e4e4', fontSize:12, fontWeight:600}}>Wetlands Inspector</div>
              <div style={{display:'grid', gridTemplateColumns:'200px 1fr', minHeight:300}}>
                <div style={{borderRight:'1px solid #e4e4e4', padding:'10px 12px', background:'#fafafa'}}>
                  <div className="muted" style={{fontSize:10, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>sidebar list</div>
                  {['Marsh A-12','Marsh B-04','Bog C-19','Tidal flat D-02','Vernal pool E-01'].map(n => (
                    <div key={n} style={{padding:'4px 6px', fontSize:11, borderBottom:'1px solid #f1f1f1', cursor:'pointer'}}>{n}</div>
                  ))}
                </div>
                <div style={{position:'relative', border:'2px solid var(--ink)', margin:6, borderRadius:4}}>
                  <div style={{position:'absolute', top:4, left:4, fontSize:9, background:'#fffae0', padding:'1px 6px', borderRadius:2, fontWeight:600, zIndex:2}}>Map · selected</div>
                  <MapPreview mode="layer" height={300} popup={false} scaleText="1:1,000,000" />
                </div>
              </div>
            </div>
          </div>

          {/* inspector for selected map component */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Map · main</h3>
              <div className="muted" style={{fontSize:10}}>page component</div>
            </div>
            <div style={{padding:'10px 12px'}}>
              <FieldGroup title="Content binding" scope="resource">
                <FieldRow state="input" label="Map item"><Sel value="◐ wetlands-overview · v2" /></FieldRow>
                <FieldRow state="input" label="Initial selection"><Sel value="from sidebar list (linked)" /></FieldRow>
              </FieldGroup>
              <FieldGroup title="Interactions">
                <FieldRow state="input" label="On feature click">
                  <Sel value="select in sidebar · open detail" />
                </FieldRow>
                <FieldRow state="input" label="On sidebar select">
                  <Sel value="zoom + highlight + open popup" />
                </FieldRow>
              </FieldGroup>
              <FieldGroup title="Layout">
                <FieldRow state="input" label="Position"><Sel value="right column · 75%" /></FieldRow>
                <FieldRow state="input" label="Min height"><Inp mono value="300 px" /></FieldRow>
              </FieldGroup>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StudioQueryBuilder() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Query','parcels-by-use-and-area','Edit']} area="studio" />
      <StudioSidebar active="query" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>◇</span>
          <input readOnly className="inp" style={{width:280, fontWeight:600}} value="parcels-by-use-and-area" />
          <Badge>draft v1</Badge>
          <span className="muted" style={{fontSize:11}}>1 source · 2 params · 8 fields</span>
          <div style={{flex:1}}/>
          <Btn ghost>Run preview</Btn>
          <Btn ghost>Save as analysis</Btn>
          <Btn kind="p">Publish…</Btn>
        </div>

        <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', flex:1, overflow:'hidden'}}>
          {/* visual builder */}
          <div style={{borderRight:'1px solid #e4e4e4', padding:'14px 18px', overflow:'auto', background:'#fafafa'}}>
            <FieldGroup title="From">
              <FieldRow state="input" label="Resource"><Sel value="◇ parcels_2024" /></FieldRow>
              <FieldRow state="calculated" label="Fields available" value="24" derivedFrom="parcels_2024" />
              <FieldRow state="calculated" label="Features" value="1,284,021" derivedFrom="parcels_2024" />
            </FieldGroup>
            <FieldGroup title="Where · filter">
              <FieldRow state="input" label="Filter expression" hint="combine predicates · AND / OR">
                <div className="col" style={{gap:4, fontSize:11}}>
                  <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #d8d8d8', borderRadius:4}}>
                    <div className="row" style={{gap:4}}>
                      <span className="mono">use_code</span>
                      <Sel value="IN" /><Inp mono value="('R-1','R-2','C-2')" />
                    </div>
                  </div>
                  <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #d8d8d8', borderRadius:4}}>
                    <div className="row" style={{gap:4}}>
                      <span className="muted" style={{fontSize:10}}>AND</span>
                      <span className="mono">area_m2</span>
                      <Sel value=">=" /><Inp mono value="$min_area" />
                    </div>
                  </div>
                  <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #d8d8d8', borderRadius:4}}>
                    <div className="row" style={{gap:4}}>
                      <span className="muted" style={{fontSize:10}}>AND</span>
                      <span className="mono">geom</span>
                      <Sel value="ST_Within" /><Inp mono value="$bbox" />
                    </div>
                  </div>
                  <Btn ghost sm>+ Predicate</Btn>
                </div>
              </FieldRow>
            </FieldGroup>
            <FieldGroup title="Select · fields">
              <FieldRow state="input" label="Columns">
                <div className="row" style={{flexWrap:'wrap', gap:4}}>
                  {['parcel_id','use_code','area_m2','assessed_value','last_assessment'].map(t => <span key={t} className="tag" style={{cursor:'pointer'}}>{t} ×</span>)}
                  <Btn ghost sm>+ Field</Btn>
                </div>
              </FieldRow>
              <FieldRow state="input" label="Order by"><Sel value="assessed_value DESC" /></FieldRow>
              <FieldRow state="input" label="Limit"><Inp mono value="5000" /></FieldRow>
            </FieldGroup>
            <FieldGroup title="Parameters · published as inputs">
              <FieldRow state="input" label="$min_area">
                <div className="row" style={{gap:4}}>
                  <Sel value="number" /><Inp mono value="default: 0" />
                </div>
              </FieldRow>
              <FieldRow state="input" label="$bbox">
                <div className="row" style={{gap:4}}>
                  <Sel value="geometry · Polygon" /><Inp mono value="default: state extent" />
                </div>
              </FieldRow>
            </FieldGroup>
          </div>

          {/* generated SQL + preview */}
          <div style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fafafa'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Generated SQL</h3>
              <div className="muted" style={{fontSize:10}}>read-only · regenerates on edit</div>
            </div>
            <pre className="mono" style={{margin:0, padding:'12px 14px', background:'#0e0e0e', color:'#d8d8d8', fontSize:10.5, lineHeight:1.55, whiteSpace:'pre-wrap', borderBottom:'1px solid #e4e4e4'}}>
{`SELECT parcel_id, use_code, area_m2, assessed_value, last_assessment
FROM   parcels_2024
WHERE  use_code IN ('R-1','R-2','C-2')
  AND  area_m2 >= :min_area
  AND  ST_Within(geom, :bbox)
ORDER  BY assessed_value DESC
LIMIT  5000`}
            </pre>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', display:'flex', alignItems:'center', gap:8}}>
              <h3 style={{margin:0, fontSize:11.5}}>Preview · 5 of 1,284 matching</h3>
              <div style={{flex:1}}/>
              <span className="muted mono" style={{fontSize:10}}>$min_area=2000 · $bbox=state</span>
              <Btn ghost sm>Edit params</Btn>
            </div>
            <table className="tbl tbl--cmpt" style={{fontSize:10.5, flex:1}}>
              <thead><tr><th>parcel_id</th><th>use</th><th className="num">area_m2</th><th className="num">assessed</th><th>last_assessment</th></tr></thead>
              <tbody>
                <tr><td className="mono">04-021-204</td><td className="mono">R-1</td><td className="num mono">2,008</td><td className="num mono">$582,000</td><td className="mono">2024-10-04</td></tr>
                <tr><td className="mono">04-021-188</td><td className="mono">C-2</td><td className="num mono">4,820</td><td className="num mono">$1,820,000</td><td className="mono">2024-09-18</td></tr>
                <tr><td className="mono">04-019-042</td><td className="mono">R-2</td><td className="num mono">2,420</td><td className="num mono">$748,000</td><td className="mono">2024-09-12</td></tr>
                <tr><td className="mono">04-021-118</td><td className="mono">R-1</td><td className="num mono">2,148</td><td className="num mono">$542,000</td><td className="mono">2024-08-12</td></tr>
                <tr><td className="mono">04-018-301</td><td className="mono">C-2</td><td className="num mono">3,920</td><td className="num mono">$1,420,000</td><td className="mono">2024-08-04</td></tr>
              </tbody>
            </table>
            <div style={{padding:'6px 12px', borderTop:'1px solid #e4e4e4', background:'#fafafa', fontSize:10.5, color:'#666'}}>
              EXPLAIN · uses <span className="mono">use_code_idx</span>, <span className="mono">geom_idx</span> · est. 124ms
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StudioAnalysisEditor() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Analysis','wetland-buffer-intersect','Edit']} area="studio" />
      <StudioSidebar active="analysis" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>∑</span>
          <input readOnly className="inp" style={{width:280, fontWeight:600}} value="wetland-buffer-intersect" />
          <Badge>draft v1</Badge>
          <span className="muted" style={{fontSize:11}}>3 steps · last run 12m ago</span>
          <div style={{flex:1}}/>
          <Btn ghost>Run with current params</Btn>
          <Btn kind="p">Publish as GP service</Btn>
        </div>

        <div style={{display:'grid', gridTemplateColumns:'260px 1fr 320px', flex:1, overflow:'hidden'}}>
          {/* step list */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Steps</h3>
            </div>
            {[
              { i:'1', n:'Inputs', t:'parcels_2024 + wetlands_2025' },
              { i:'2', n:'Buffer wetlands', t:'ST_Buffer · $buffer_m', sel:true },
              { i:'3', n:'Intersect', t:'ST_Intersects · parcels ∩ buffer' },
              { i:'4', n:'Aggregate', t:'sum(assessed_value) by use_code' },
              { i:'5', n:'Outputs', t:'table + map layer' },
            ].map(s => (
              <div key={s.i} className="row" style={{
                padding:'7px 12px', borderBottom:'1px solid #f1f1f1',
                background: s.sel ? '#fffae0' : 'transparent',
                borderLeft: s.sel ? '3px solid var(--ink)' : '3px solid transparent',
                cursor:'pointer', fontSize:11
              }}>
                <span style={{width:18, textAlign:'center', fontWeight:600}}>{s.i}</span>
                <div style={{flex:1, minWidth:0}}>
                  <div style={{fontWeight: s.sel ? 600 : 500}}>{s.n}</div>
                  <div className="muted" style={{fontSize:10}}>{s.t}</div>
                </div>
              </div>
            ))}
          </div>

          {/* canvas — pipeline with preview at the bottom */}
          <div style={{display:'flex', flexDirection:'column', overflow:'hidden', background:'#fafafa'}}>
            <div style={{padding:'18px 20px', flex:'0 0 auto'}}>
              <div style={{position:'relative', minHeight:160}}>
                <svg width="100%" height="160" viewBox="0 0 740 160" style={{position:'absolute', inset:0}}>
                  <defs>
                    <marker id="anaa" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
                      <path d="M 0 0 L 10 5 L 0 10 z" fill="#141414" />
                    </marker>
                  </defs>
                  {[[110,80,240,80],[240,80,370,80],[370,80,500,80],[500,80,630,80]].map((e,i) => (
                    <path key={i} d={`M ${e[0]} ${e[1]} L ${e[2]} ${e[3]}`} stroke="#141414" strokeWidth="1.5" fill="none" markerEnd="url(#anaa)" />
                  ))}
                </svg>
                {[
                  { x:30, y:50, t:'📥', n:'Inputs', sub:'2 resources' },
                  { x:160, y:50, t:'⬢', n:'Buffer', sub:'ST_Buffer $buffer_m', sel:true },
                  { x:290, y:50, t:'∩', n:'Intersect', sub:'parcels ∩ buffer' },
                  { x:420, y:50, t:'Σ', n:'Aggregate', sub:'by use_code' },
                  { x:550, y:50, t:'📤', n:'Outputs', sub:'table + layer' },
                ].map((n,i) => (
                  <div key={i} style={{
                    position:'absolute', left:n.x, top:n.y, width:110, padding:8,
                    background: n.sel ? '#fffae0' : '#fff',
                    border: n.sel ? '2px solid var(--ink)' : '1.5px solid #141414',
                    borderRadius:6, fontSize:11, textAlign:'center'
                  }}>
                    <div style={{fontSize:14, marginBottom:2}}>{n.t}</div>
                    <div style={{fontWeight:600}}>{n.n}</div>
                    <div className="muted" style={{fontSize:9.5, marginTop:1}}>{n.sub}</div>
                  </div>
                ))}
              </div>
            </div>

            <div style={{borderTop:'1px solid #e4e4e4', padding:'8px 14px', background:'#fff', display:'flex', alignItems:'center', gap:8}}>
              <h3 style={{margin:0, fontSize:11.5}}>Run preview</h3>
              <span className="muted" style={{fontSize:10.5}}>$buffer_m = 500 · 12m ago · 4,218 parcels intersected</span>
              <div style={{flex:1}}/>
              <Btn ghost sm>Re-run</Btn>
            </div>
            <div style={{flex:1, padding:14, overflow:'auto'}}>
              <MapPreview mode="layer" height={240} popup={false} scaleText="1:80,000" />
            </div>
          </div>

          {/* inspector for buffer step */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Buffer wetlands</h3>
              <div className="muted" style={{fontSize:10}}>step 2 · ST_Buffer</div>
            </div>
            <div style={{padding:'10px 12px'}}>
              <FieldGroup title="Inputs">
                <FieldRow state="input" label="Input layer"><Sel value="$inputs.wetlands_2025" /></FieldRow>
              </FieldGroup>
              <FieldGroup title="Parameters">
                <FieldRow state="input" label="$buffer_m" hint="published as analysis input">
                  <div className="row" style={{gap:6}}>
                    <Sel value="number · meters" />
                    <Inp mono value="default: 500" />
                  </div>
                </FieldRow>
                <FieldRow state="input" label="Cap style"><Sel value="round" /></FieldRow>
                <FieldRow state="input" label="Quad segs"><Inp mono value="8" /></FieldRow>
              </FieldGroup>
              <FieldGroup title="Outputs">
                <FieldRow state="calculated" label="$step2.buffer" value="Polygon · same CRS as input" derivedFrom="step" />
                <FieldRow state="calculated" label="$step2.area_total" value="number · m²" derivedFrom="step" />
              </FieldGroup>
              <Callout kind="info" style={{margin:0}}>
                Step outputs are referenceable downstream as <span className="mono">$step2.&lt;name&gt;</span>. Publish-as-GP exposes <span className="mono">$buffer_m</span> as a public input parameter.
              </Callout>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { StudioReportEditor, StudioAppEditor, StudioQueryBuilder, StudioAnalysisEditor });
