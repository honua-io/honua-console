// IA map + Dashboard A/B

function IAMap() {
  // Big diagram. Use SVG with positioned div nodes.
  return (
    <div style={{padding:'20px 24px', overflow:'auto', height:'100%', background:'#fcfcfa', position:'relative'}}>
      <div style={{marginBottom:14}}>
        <h2 style={{margin:'0 0 4px', font:'600 16px/1.1 var(--ui)'}}>Information architecture</h2>
        <div className="muted" style={{fontSize:11.5}}>
          The operator flow. Linear left-to-right; one canonical resource, many publish targets. Tabs inside Data Resource are grouped Define / Publish / Operate.
        </div>
      </div>

      {/* Flow rail */}
      <div style={{position:'relative', minHeight: 380}}>
        <svg width="100%" height="380" viewBox="0 0 1180 380" style={{position:'absolute', inset:0}}>
          {/* horizontal flow */}
          <path d="M 90 60 L 250 60 L 250 60" className="ia-line" />
          <path d="M 90 60 L 1140 60" className="ia-line" strokeDasharray="0" />
          {/* arrowheads */}
          {[230, 390, 560, 720, 880, 1040, 1140].map((x,i) => (
            <path key={i} d={`M ${x-5} 56 L ${x} 60 L ${x-5} 64`} className="ia-line" />
          ))}
          {/* drop lines to Operate row */}
          <path d="M 250 80 L 250 200" className="ia-line" strokeDasharray="3 3" />
          <path d="M 410 80 L 410 200" className="ia-line" strokeDasharray="3 3" />
          <path d="M 580 80 L 580 200" className="ia-line" strokeDasharray="3 3" />
          <path d="M 740 80 L 740 200" className="ia-line" strokeDasharray="3 3" />
          <path d="M 900 80 L 900 200" className="ia-line" strokeDasharray="3 3" />

          {/* operate row connecting */}
          <path d="M 100 240 L 1080 240" className="ia-line" />
          <path d="M 100 240 L 100 305" className="ia-line" />
          <path d="M 1080 240 L 1080 305" className="ia-line" />
        </svg>

        {/* primary flow nodes */}
        {[
          { x: 30, y: 40, t: '① Connect', s: 'Connections', leaf: false },
          { x: 195, y: 40, t: '② Source', s: 'Tables / Files / Remote', leaf: false },
          { x: 360, y: 40, t: '③ Resource', s: 'Define metadata', leaf: false, root: true },
          { x: 530, y: 40, t: '④ Publish', s: 'Services / Catalogs', leaf: false },
          { x: 695, y: 40, t: '⑤ Access', s: 'Roles / Sharing', leaf: false },
          { x: 855, y: 40, t: '⑥ Validate', s: 'Quality / Policy', leaf: false },
          { x: 1015, y: 40, t: '⑦ Activity', s: 'Jobs / Logs', leaf: false },
        ].map((n, i) => (
          <div key={i} style={{position:'absolute', left: n.x, top: n.y, width:150}}>
            <div className={'ia-node' + (n.root ? ' ia-node--root' : '')} style={{width:'100%', justifyContent:'center'}}>
              {n.t}
            </div>
            <div style={{textAlign:'center', fontSize:10.5, color:'#888', marginTop:4}}>{n.s}</div>
          </div>
        ))}

        {/* secondary nodes */}
        {[
          { x: 200, y: 200, t: 'Postgres' },
          { x: 200, y: 226, t: 'SQL Server' },
          { x: 200, y: 252, t: 'S3 / object store' },
          { x: 360, y: 200, t: 'Table import' },
          { x: 360, y: 226, t: 'File upload' },
          { x: 360, y: 252, t: 'Remote service' },
          { x: 530, y: 200, t: 'Fields' },
          { x: 530, y: 226, t: 'Metadata' },
          { x: 530, y: 252, t: 'Presentation' },
          { x: 695, y: 200, t: 'Folder' },
          { x: 695, y: 226, t: '  Service (FS, OGC API…)' },
          { x: 695, y: 252, t: '    Layer slot → resource' },
          { x: 855, y: 200, t: 'Public / Private' },
          { x: 855, y: 226, t: 'Roles' },
          { x: 855, y: 252, t: 'CORS / Auth' },
        ].map((n,i) => (
          <div key={i} style={{
            position:'absolute', left:n.x, top:n.y,
            font:'500 10.5px var(--ui)', color:'#555',
            border:'1px solid #ccc', borderRadius:3,
            padding:'2px 6px', background:'#fff', width:128
          }}>{n.t}</div>
        ))}

        {/* operate row */}
        <div style={{position:'absolute', left:60, top:295, width:1040,
          border:'1.2px dashed #888', borderRadius:6, padding:'10px 14px', background:'#fdfaeb'}}>
          <div style={{font:'600 11px var(--ui)', marginBottom:6}}>
            Cross-cutting: Activity center (Jobs + Validation, unified)
          </div>
          <div className="row" style={{gap:6, flexWrap:'wrap'}}>
            {['Imports','Publishes','Refreshes','Schema scans','Policy checks','Webhooks','Audit'].map(t => (
              <span key={t} className="tag">{t}</span>
            ))}
          </div>
        </div>

        <div className="ann ann--margin" style={{left: 360, top: 6}}>
          Data Resource = semantic center.<br/>everything else hangs off it.
        </div>
        <div className="ann ann--margin ann--red" style={{left: 690, top: 6, color: 'var(--redline)'}}>
          publish = one→many.<br/>see matrix.
        </div>
      </div>

      {/* legend */}
      <div style={{marginTop:18, display:'grid', gridTemplateColumns:'repeat(3,1fr)', gap:12}}>
        <div className="card">
          <h3>What's intentionally hidden</h3>
          <div className="muted" style={{fontSize:11}}>
            Internal terms like storageBinding, projectionProfile, ABAC, distribution object, runtime snapshot — never surfaced as primary labels. Power users see them under Advanced.
          </div>
        </div>
        <div className="card">
          <h3>What's not in v1</h3>
          <div className="muted" style={{fontSize:11}}>
            No org / workspace / tenant switcher. No marketing surfaces. No raw schema editor as the primary path.
          </div>
        </div>
        <div className="card">
          <h3>Primary noun</h3>
          <div className="muted" style={{fontSize:11}}>
            "Data Resource". One canonical metadata model → many publish formats. Operators think in resources, not in distributions.
          </div>
        </div>
      </div>
    </div>
  );
}

function StatTile({ label, value, sub, tone }) {
  return (
    <div className="card" style={{gap:4}}>
      <div className="muted" style={{fontSize:10.5,textTransform:'uppercase',letterSpacing:'0.06em'}}>{label}</div>
      <div style={{font:'600 22px/1 var(--ui)', letterSpacing:'-0.02em'}}>{value}</div>
      <div className="muted" style={{fontSize:10.5}}>
        {tone === 'up' && <span style={{color:'var(--ok)'}}>▲ </span>}
        {tone === 'down' && <span style={{color:'var(--bad)'}}>▼ </span>}
        {sub}
      </div>
    </div>
  );
}

function DashboardA() {
  return (
    <div className="scr">
      <TopBar crumbs={['Dashboard']} />
      <Sidebar active="dashboard" />
      <div className="main">
        <PageHead
          title="Good morning, Jamie"
          sub="Tue · 14 May 2026 · 4 things need a look"
          actions={<>
            <Btn>Import data</Btn>
            <Btn kind="p" ico="+">New resource</Btn>
          </>}
        />
        <div style={{padding:'14px 18px', overflow:'auto'}}>
          <div style={{display:'grid', gridTemplateColumns:'repeat(4,1fr)', gap:10, marginBottom:14}}>
            <StatTile label="Data resources" value="128" sub="3 added this week" tone="up" />
            <StatTile label="Published targets" value="312" sub="across 9 services" />
            <StatTile label="Active jobs" value="3" sub="2 imports, 1 publish" />
            <StatTile label="Validation issues" value="6" sub="2 blocking" tone="down" />
          </div>

          <div style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:10}}>
            <div className="card" style={{padding:0}}>
              <div style={{padding:'10px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
                <h3 style={{margin:0}}>Needs your attention</h3>
                <div style={{flex:1}} />
                <span className="muted" style={{fontSize:11}}>4 items</span>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th>Resource</th><th>What</th><th>Why</th><th>When</th><th></th>
                </tr></thead>
                <tbody>
                  <tr><td><b>parcels_2024</b></td><td><Badge kind="bad">Blocked publish</Badge></td><td>Missing CRS on 2 fields</td><td className="muted">3m ago</td><td><Btn sm>Fix</Btn></td></tr>
                  <tr><td><b>fire_perimeters</b></td><td><Badge kind="warn">Drift detected</Badge></td><td>Source schema changed</td><td className="muted">28m ago</td><td><Btn sm>Review</Btn></td></tr>
                  <tr><td><b>monitoring_sites</b></td><td><Badge kind="warn">Validation</Badge></td><td>14 rows with null geom</td><td className="muted">1h ago</td><td><Btn sm>Open</Btn></td></tr>
                  <tr><td><b>watersheds_v3</b></td><td><Badge kind="info">Awaiting review</Badge></td><td>K. requested approval</td><td className="muted">3h ago</td><td><Btn sm>Review</Btn></td></tr>
                </tbody>
              </table>
            </div>

            <div className="card">
              <div className="row"><h3>Activity</h3><div style={{flex:1}}/><span className="muted" style={{fontSize:11}}>last 24h</span></div>
              <div style={{display:'flex',flexDirection:'column',gap:8}}>
                {[
                  { st: 'ok', t: 'Published parcels_2024 → OGC Features', m: '2m', who: 'system' },
                  { st: 'run', t: 'Importing fire_obs.csv (62%)', m: 'now', who: 'jamie' },
                  { st: 'ok', t: 'Refreshed wetlands_2025', m: '14m', who: 'system' },
                  { st: 'bad', t: 'Failed: publish tile cache', m: '22m', who: 'system' },
                  { st: 'ok', t: 'Connection synced: ArcGIS Online', m: '1h', who: 'system' },
                ].map((a,i) => (
                  <div key={i} className="row" style={{fontSize:11, padding:'4px 0', borderBottom:'1px dashed #eee'}}>
                    <span style={{
                      width:8, height:8, borderRadius:'50%',
                      background: a.st === 'ok' ? 'var(--ok)' : a.st === 'bad' ? 'var(--bad)' : 'var(--warn)'
                    }} />
                    <span style={{flex:1, overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap'}}>{a.t}</span>
                    <span className="muted" style={{fontSize:10}}>{a.m}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div style={{display:'grid', gridTemplateColumns:'1fr 1fr 1fr', gap:10, marginTop:10}}>
            <div className="card">
              <h3>Pinned resources</h3>
              <div style={{display:'flex', flexDirection:'column', gap:4}}>
                {['parcels_2024','wetlands_2025','fire_perimeters','obs_stations'].map(t => (
                  <div key={t} className="row" style={{fontSize:11, padding:'4px 0'}}>
                    <span>◇</span><span style={{flex:1}}>{t}</span>
                    <Badge kind="ok">Published</Badge>
                  </div>
                ))}
              </div>
            </div>
            <div className="card">
              <h3>Quick actions</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr', gap:6}}>
                <Btn kind="a">⚡ Publish service</Btn>
                <Btn>+ Connection</Btn>
                <Btn>+ From table</Btn>
                <Btn>+ From file</Btn>
                <Btn>+ Import remote</Btn>
                <Btn>+ Role</Btn>
              </div>
              <div className="muted" style={{fontSize:10.5, marginTop:4}}>
                <b>Publish service</b> = one-shot. Skip authoring a resource first.
              </div>
            </div>
            <div className="card">
              <h3>System health</h3>
              <div className="row" style={{fontSize:11}}><span style={{flex:1}}>Storage</span><span className="mono">142 / 500 GB</span></div>
              <Bar pct={28} />
              <div className="row" style={{fontSize:11, marginTop:6}}><span style={{flex:1}}>Tile cache</span><span className="mono">68%</span></div>
              <Bar pct={68} />
              <div className="row" style={{fontSize:11, marginTop:6}}><span style={{flex:1}}>License</span><span className="muted">expires 2027-02-01</span></div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function DashboardB() {
  // Variant: "what's running" + map-led, more operations-room feel
  return (
    <div className="scr">
      <TopBar crumbs={['Dashboard']} />
      <Sidebar active="dashboard" />
      <div className="main">
        <PageHead
          title="System overview"
          sub="Live · auto-refresh 10s"
          actions={<><Btn>Filters</Btn><Btn kind="p">Open activity</Btn></>}
        />
        <div style={{padding:'12px 18px', overflow:'auto', display:'grid', gridTemplateRows:'auto auto 1fr', gap:10}}>
          {/* row 1: status bar */}
          <div style={{display:'grid', gridTemplateColumns:'repeat(6,1fr)', gap:8}}>
            {[
              { l: 'Services', v: '9', s: <Badge kind="ok">all up</Badge> },
              { l: 'Resources', v: '128', s: <span className="muted">3 staged</span> },
              { l: 'Jobs running', v: '3', s: <span className="muted">queue 0</span> },
              { l: 'Issues', v: '6', s: <Badge kind="warn">2 blocking</Badge> },
              { l: 'Auth', v: 'OIDC', s: <Badge kind="ok">healthy</Badge> },
              { l: 'API p95', v: '184ms', s: <span className="muted">vs 210ms 7d</span> },
            ].map((t,i) => (
              <div key={i} className="card" style={{padding:'8px 10px',gap:2}}>
                <div className="muted" style={{fontSize:10}}>{t.l.toUpperCase()}</div>
                <div style={{font:'600 16px var(--ui)'}}>{t.v}</div>
                <div style={{fontSize:10}}>{t.s}</div>
              </div>
            ))}
          </div>

          {/* row 2: map + jobs */}
          <div style={{display:'grid', gridTemplateColumns:'1.6fr 1fr', gap:10}}>
            <div className="card" style={{padding:0, overflow:'hidden'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center', gap:8}}>
                <h3>Resource footprint</h3>
                <span className="muted" style={{fontSize:11}}>where your published data lives</span>
                <div style={{flex:1}}/>
                <FiltChip>format: all</FiltChip><FiltChip>region: all</FiltChip>
              </div>
              <Ph style={{minHeight:240, borderRadius:0, borderTop:'none',borderLeft:'none',borderRight:'none',borderBottom:'1px dashed #c5c5c5'}}>
                world / coverage map · clustered counts
              </Ph>
              <div className="row" style={{padding:'8px 12px', gap:14, fontSize:11}}>
                <span><b>128</b> resources</span>
                <span><b>312</b> publish targets</span>
                <span><b>6.4M</b> features</span>
                <span><b>142 GB</b> storage</span>
                <div style={{flex:1}}/>
                <Ann>this could also live in resources list as a toggle.</Ann>
              </div>
            </div>

            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3>Jobs · running</h3>
              </div>
              <div style={{padding:'10px 12px', display:'flex', flexDirection:'column', gap:10}}>
                {[
                  { t: 'Import · fire_obs.csv → fire_observations', p: 62, eta: '4m', tone:'run' },
                  { t: 'Publish · parcels_2024 → OGC Features', p: 88, eta: '40s', tone:'run' },
                  { t: 'Refresh · wetlands_2025', p: 12, eta: '12m', tone:'run' },
                ].map((j,i) => (
                  <div key={i}>
                    <div className="row" style={{fontSize:11}}>
                      <span style={{flex:1, overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap'}}>{j.t}</span>
                      <span className="muted mono" style={{fontSize:10}}>{j.p}% · {j.eta}</span>
                    </div>
                    <Bar pct={j.p} />
                  </div>
                ))}
                <div className="divider" />
                <div className="row"><span className="muted" style={{fontSize:11,flex:1}}>Queued</span><span className="muted mono">0</span></div>
                <div className="row"><span className="muted" style={{fontSize:11,flex:1}}>Failed in last 24h</span><span className="mono">1</span></div>
              </div>
            </div>
          </div>

          {/* row 3: recent + validation */}
          <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3>Recently published</h3>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>Resource</th><th>Target</th><th>By</th><th>When</th></tr></thead>
                <tbody>
                  <tr><td><b>parcels_2024</b></td><td>OGC Features · v4</td><td>jamie</td><td className="muted">2m</td></tr>
                  <tr><td><b>parcels_2024</b></td><td>STAC catalog</td><td>jamie</td><td className="muted">2m</td></tr>
                  <tr><td><b>obs_stations</b></td><td>Tile service</td><td>system</td><td className="muted">14m</td></tr>
                  <tr><td><b>watersheds_v3</b></td><td>OGC Features</td><td>k.tan</td><td className="muted">2h</td></tr>
                </tbody>
              </table>
            </div>
            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3>Validation · open issues</h3>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>Severity</th><th>Resource</th><th>Rule</th><th>Count</th></tr></thead>
                <tbody>
                  <tr><td><Badge kind="bad">Block</Badge></td><td>parcels_2024</td><td>CRS required</td><td>2</td></tr>
                  <tr><td><Badge kind="bad">Block</Badge></td><td>watersheds_v3</td><td>Geometry valid</td><td>1</td></tr>
                  <tr><td><Badge kind="warn">Warn</Badge></td><td>fire_perimeters</td><td>Schema drift</td><td>1</td></tr>
                  <tr><td><Badge kind="warn">Warn</Badge></td><td>monitoring_sites</td><td>Non-null geom</td><td>14</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { IAMap, DashboardA, DashboardB });
