// Issue #122 · #102 — "Import from Esri" wizard + parity scorecard (Operate)
// screens-import-wizard.jsx
// Multi-step: source connect → select content → map → run → scorecard.
// Covers: empty · loading · run progress (resumable/retry) · partial · error · missing-binding.

function ImportEsriWizard() {
  // Step 3 of 5 (Map) — the meaty step. Shows mixed content types + per-item fidelity.
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Operate','Import from Esri']} area="operate" />
      <div className="wiz">
        <Stepper steps={['Source','Select content','Map','Run','Scorecard']} on={2} />

        {/* source context */}
        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span className="tag">ArcGIS Online</span>
          <b>org.maps.arcgis.com / state-gis</b>
          <span className="muted">· 18 items selected · 3 content types</span>
          <div style={{flex:1}}/>
          <Badge kind="ok">connected</Badge>
          <span className="muted" style={{fontSize:10.5}}>migration run → honua-devops</span>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, overflow:'auto'}}>
          <div>
            <div className="row" style={{marginBottom:6}}>
              <h2 style={{margin:0, font:'600 16px var(--ui)'}}>Map selected content</h2>
              <div style={{flex:1}}/>
              <FiltChip on x>type: all</FiltChip>
              <FiltChip>issues only</FiltChip>
            </div>
            <div className="muted" style={{fontSize:11.5, marginBottom:12}}>
              Each item is converted to a Console content package. Per-item fidelity shown; items that need a data binding are flagged.
            </div>

            <table className="tbl tbl--cmpt">
              <thead><tr>
                <th style={{width:24}}><input type="checkbox" readOnly defaultChecked /></th>
                <th>Esri item</th><th>Type</th><th>→ Console</th><th>Fidelity</th><th>Blockers</th>
              </tr></thead>
              <tbody>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Public Works Web Map</b></td><td>Web Map</td><td className="mono">map package</td><td><Fid k="clean" /></td><td className="muted">—</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Parcels FeatureServer</b></td><td>Feature Service</td><td className="mono">data resource</td><td><Fid k="clean" /></td><td className="muted">migrate · PostGIS</td></tr>
                <tr style={{background:'#fff7e6'}}><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Q3 Ops Dashboard</b></td><td>Dashboard</td><td className="mono">dashboard package</td><td><Fid k="degrade" /></td><td>1 gauge degrades · 1 iframe dropped</td></tr>
                <tr style={{background:'#fff7e6'}}><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Watershed StoryMap</b></td><td>StoryMap</td><td className="mono">report</td><td><Fid k="degrade" /></td><td>swipe → static · video dropped</td></tr>
                <tr style={{background:'#eaf0fb'}}><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Hydrants Web Map</b></td><td>Web Map</td><td className="mono">map package</td><td><Fid k="manual" /></td><td>1 layer unbound · pick resource</td></tr>
                <tr style={{background:'#fbeae7'}}><td><input type="checkbox" readOnly /></td><td><b>3D Scene · terrain</b></td><td>Web Scene</td><td className="muted">—</td><td><Fid k="drop" /></td><td>3D scenes not supported in v1</td></tr>
                <tr><td colSpan="6" className="muted" style={{textAlign:'center', padding:6, fontSize:10.5}}>+ 12 more items</td></tr>
              </tbody>
            </table>
          </div>

          <div className="col">
            <div className="card">
              <h3>Conversion summary</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><Fid k="clean" /><span style={{flex:1, marginLeft:6}}>converts clean</span><b>11</b></div>
                <div className="row"><Fid k="degrade" /><span style={{flex:1, marginLeft:6}}>degrades</span><b>4</b></div>
                <div className="row"><Fid k="manual" /><span style={{flex:1, marginLeft:6}}>needs binding</span><b>2</b></div>
                <div className="row"><Fid k="drop" /><span style={{flex:1, marginLeft:6}}>can't import</span><b>1</b></div>
              </div>
            </div>
            <Callout kind="info">
              <b>Migration run owner.</b> This wizard hands off to <span className="mono">honua-devops</span> which drives the run. You can close the tab — progress resumes on the Run step or in Event Viewer.
            </Callout>
            <Callout kind="warn">2 items need a data resource binding before their maps render. You can bind now or import as drafts and resolve later.</Callout>
            <Ann red>nothing is written until you hit Run. dry preview only.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Select content</Btn>
          <div className="row">
            <Btn ghost>Save selection</Btn>
            <Btn kind="p">Run migration →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function ImportRunProgress() {
  // Step 4 (Run) — progress, resumable, retry.
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Operate','Import from Esri','Run']} area="operate" />
      <div className="wiz">
        <Stepper steps={['Source','Select content','Map','Run','Scorecard']} on={3} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="warn">running · 11 of 18</Badge>
          <span style={{flex:1}}>Migration <span className="mono">mig_4830</span> · driven by honua-devops · started 4m ago · resumable</span>
          <Btn ghost sm>Pause</Btn>
          <Btn ghost sm>Run in background</Btn>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, overflow:'auto'}}>
          <div className="col">
            <Bar pct={61} />
            <div className="muted" style={{fontSize:11, margin:'4px 0 10px'}}>11 of 18 items · ~4 min remaining · 1 failed (retryable)</div>

            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}><h3 style={{margin:0}}>Per-item progress</h3></div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>Item</th><th>Type</th><th>State</th><th>Result</th><th></th></tr></thead>
                <tbody>
                  <tr><td>Public Works Web Map</td><td>map</td><td><Badge kind="ok">done</Badge></td><td className="mono">◐ public-works-overview</td><td><a style={{fontSize:10.5,cursor:'pointer'}}>Open</a></td></tr>
                  <tr><td>Parcels FeatureServer</td><td>data</td><td><Badge kind="ok">done</Badge></td><td className="mono">◇ parcels_2024</td><td><a style={{fontSize:10.5,cursor:'pointer'}}>Open</a></td></tr>
                  <tr><td>Q3 Ops Dashboard</td><td>dashboard</td><td><Badge kind="ok">done</Badge></td><td className="mono">▤ q3-operations · 1 warn</td><td><a style={{fontSize:10.5,cursor:'pointer'}}>Open</a></td></tr>
                  <tr style={{background:'#fff7e6'}}><td>Watershed StoryMap</td><td>report</td><td><Badge kind="warn">running</Badge></td><td><Bar pct={40} /></td><td className="muted">media…</td></tr>
                  <tr style={{background:'#fbeae7'}}><td>Roads FeatureServer</td><td>data</td><td><Badge kind="bad">failed</Badge></td><td>geometry repair error · row 1.2M</td><td><Btn sm>Retry</Btn></td></tr>
                  <tr><td>Hydrants Web Map</td><td>map</td><td><Badge>queued</Badge></td><td className="muted">waiting</td><td></td></tr>
                  <tr><td colSpan="5" className="muted" style={{textAlign:'center', padding:6, fontSize:10.5}}>+ 6 queued</td></tr>
                </tbody>
              </table>
            </div>
          </div>

          <div className="col">
            <Callout kind="bad"><b>1 item failed.</b> Roads FeatureServer hit a geometry repair error. Retry just that item, or skip and continue — the run is resumable.</Callout>
            <div className="card">
              <h3>Run</h3>
              <dl className="kv">
                <dt>Migration ID</dt><dd className="mono">mig_4830</dd>
                <dt>Driver</dt><dd className="mono">honua-devops</dd>
                <dt>Target env</dt><dd>dev</dd>
                <dt>Started by</dt><dd>jamie</dd>
                <dt>Resumable</dt><dd><Badge kind="ok">yes</Badge></dd>
              </dl>
              <Btn ghost sm>View in Event Viewer ↗</Btn>
            </div>
            <Ann>closing the tab is safe — run continues server-side.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Map</Btn>
          <div className="row">
            <Btn ghost>Skip failed · continue</Btn>
            <Btn kind="p">View scorecard →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function ParityScorecard() {
  // Step 5 (Scorecard) — per-item pass/degraded/failed + drill-down + export.
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Operate','Import from Esri','Parity scorecard']} area="operate" />
      <div className="wiz">
        <Stepper steps={['Source','Select content','Map','Run','Scorecard']} on={4} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="ok">run complete</Badge>
          <span style={{flex:1}}>Migration <span className="mono">mig_4830</span> · 18 items · 8m 14s</span>
          <Btn ghost sm>Export report (PDF)</Btn>
          <Btn ghost sm>Export findings (CSV)</Btn>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, overflow:'auto'}}>
          <div className="col">
            {/* scorecard summary */}
            <div style={{display:'grid', gridTemplateColumns:'repeat(4, 1fr)', gap:8, marginBottom:4}}>
              {[
                ['Passed','11','ok'],
                ['Degraded','4','warn'],
                ['Needs binding','2','info'],
                ['Failed','1','bad'],
              ].map((t,i) => (
                <div key={i} className="card" style={{padding:'8px 10px', gap:2}}>
                  <div className="muted" style={{fontSize:9.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>{t[0]}</div>
                  <div className="row">
                    <div style={{font:'600 20px var(--ui)'}}>{t[1]}</div>
                    <div style={{flex:1}}/>
                    {t[2]==='ok' && <Badge kind="ok">●</Badge>}
                    {t[2]==='warn' && <Badge kind="warn">●</Badge>}
                    {t[2]==='info' && <Badge kind="info">●</Badge>}
                    {t[2]==='bad' && <Badge kind="bad">●</Badge>}
                  </div>
                </div>
              ))}
            </div>

            {/* overall parity bar */}
            <div className="card">
              <div className="row" style={{marginBottom:6}}>
                <h3 style={{margin:0}}>Overall parity</h3>
                <div style={{flex:1}}/>
                <b style={{fontSize:14}}>83%</b>
              </div>
              <div style={{display:'flex', height:14, borderRadius:7, overflow:'hidden', border:'1px solid var(--ink)'}}>
                <div style={{width:'61%', background:'var(--ok)'}} />
                <div style={{width:'22%', background:'var(--warn)'}} />
                <div style={{width:'11%', background:'var(--pencil)'}} />
                <div style={{width:'6%', background:'var(--bad)'}} />
              </div>
              <div className="row" style={{gap:12, fontSize:10, marginTop:4}}>
                <span className="row" style={{gap:4}}><span style={{width:8,height:8,background:'var(--ok)'}}/>passed</span>
                <span className="row" style={{gap:4}}><span style={{width:8,height:8,background:'var(--warn)'}}/>degraded</span>
                <span className="row" style={{gap:4}}><span style={{width:8,height:8,background:'var(--pencil)'}}/>binding</span>
                <span className="row" style={{gap:4}}><span style={{width:8,height:8,background:'var(--bad)'}}/>failed</span>
              </div>
            </div>

            {/* per-item findings */}
            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}><h3 style={{margin:0}}>Per-item findings</h3></div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>Item</th><th>Result</th><th>Findings</th><th>Output</th><th></th></tr></thead>
                <tbody>
                  <tr><td><b>Public Works Web Map</b></td><td><Badge kind="ok">pass</Badge></td><td className="muted">7 layers · 0 issues</td><td className="mono">◐ public-works-overview</td><td><a style={{fontSize:10.5,cursor:'pointer'}}>Open</a></td></tr>
                  <tr style={{background:'#fff7e6'}}><td><b>Q3 Ops Dashboard</b></td><td><Badge kind="warn">degraded</Badge></td><td>gauge → radial KPI · 1 iframe dropped</td><td className="mono">▤ q3-operations</td><td><a style={{fontSize:10.5,cursor:'pointer'}}>Findings</a></td></tr>
                  <tr style={{background:'#fff7e6'}}><td><b>Watershed StoryMap</b></td><td><Badge kind="warn">degraded</Badge></td><td>swipe → static · 1 video dropped</td><td className="mono">⊟ watershed-health</td><td><a style={{fontSize:10.5,cursor:'pointer'}}>Findings</a></td></tr>
                  <tr style={{background:'#eaf0fb'}}><td><b>Hydrants Web Map</b></td><td><Badge kind="info">binding</Badge></td><td>1 layer unbound · Hydrants</td><td className="mono">◐ hydrants-map (draft)</td><td><Btn sm>Bind</Btn></td></tr>
                  <tr style={{background:'#fbeae7'}}><td><b>Roads FeatureServer</b></td><td><Badge kind="bad">failed</Badge></td><td>geometry repair error · row 1.2M</td><td className="muted">—</td><td><Btn sm>Retry</Btn></td></tr>
                  <tr style={{background:'#fbeae7'}}><td><b>3D Scene · terrain</b></td><td><Badge kind="bad">skipped</Badge></td><td>Web Scene unsupported in v1</td><td className="muted">—</td><td><span className="muted">⋯</span></td></tr>
                </tbody>
              </table>
            </div>
          </div>

          <div className="col">
            <Callout kind="info">
              <b>Scorecard is the migration record.</b> Exported report attaches to the migration event in Event Viewer. Re-runnable: fix bindings / retry failures and re-import just those items.
            </Callout>
            <div className="card">
              <h3>Next steps</h3>
              <ol style={{margin:'4px 0 0 16px', padding:0, fontSize:11, lineHeight:1.7}}>
                <li>Bind 2 unbound layers (Hydrants)</li>
                <li>Retry 1 failed data migration (Roads)</li>
                <li>Review 4 degraded items in Studio</li>
                <li>Promote dev → staging when satisfied</li>
              </ol>
            </div>
            <div className="card">
              <h3>What landed</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><span style={{flex:1}}>Map packages</span><b>4</b></div>
                <div className="row"><span style={{flex:1}}>Dashboards</span><b>1</b></div>
                <div className="row"><span style={{flex:1}}>Reports</span><b>1</b></div>
                <div className="row"><span style={{flex:1}}>Data resources</span><b>5</b></div>
                <div className="row"><span style={{flex:1}}>Drafts (need work)</span><b>3</b></div>
              </div>
            </div>
            <Ann red>degraded ≠ broken. each degraded item is usable; findings list exactly what changed.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Run</Btn>
          <div className="row">
            <Btn ghost>Export report</Btn>
            <Btn kind="a">Finish · open Catalog</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function ImportMissingBinding() {
  // Charter §11 — missing-binding / no-honua-server state. Never mocked data.
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Operate','Import from Esri']} area="operate" />
      <div style={{flex:1, display:'grid', placeItems:'center', padding:24, background:'#fafafa'}}>
        <div style={{maxWidth:520, textAlign:'center'}}>
          <div style={{width:48, height:48, border:'1.5px solid var(--warn)', borderRadius:'50%', display:'grid', placeItems:'center', fontSize:22, color:'var(--warn)', background:'#fff7e6', margin:'0 auto 14px'}}>!</div>
          <h2 style={{font:'600 16px var(--ui)', margin:'0 0 6px'}}>No honua-server is configured</h2>
          <div className="muted" style={{fontSize:12, lineHeight:1.6, marginBottom:16}}>
            The Esri import wizard drives a migration run against a honua-server in the selected environment. This Console isn't bound to one yet, so there's nothing to import into. We never show mocked data here.
          </div>
          <div className="card" style={{textAlign:'left', marginBottom:14}}>
            <h3>To continue</h3>
            <ol style={{margin:'4px 0 0 16px', padding:0, fontSize:11.5, lineHeight:1.7}}>
              <li>Connect a honua-server in <span className="mono">Operate → Environments</span></li>
              <li>Or switch to an environment that already has one bound</li>
              <li>Then re-open <span className="mono">Import from Esri</span></li>
            </ol>
          </div>
          <div className="row" style={{justifyContent:'center', gap:8}}>
            <Btn>Switch environment</Btn>
            <Btn kind="p">Connect honua-server →</Btn>
          </div>
          <Ann style={{justifyContent:'center', marginTop:14}}>this is the binding-required state per Charter §11 · shown instead of an empty wizard.</Ann>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ImportEsriWizard, ImportRunProgress, ParityScorecard, ImportMissingBinding });
