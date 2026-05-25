// Activity center (unified jobs+validation runs+events) + job detail + Validation center

function Activity() {
  return (
    <div className="scr">
      <TopBar crumbs={['Activity']} />
      <Sidebar active="activity" />
      <div className="main">
        <PageHead
          title="Activity"
          sub="Everything running, queued, or recently finished. Imports, publishes, refreshes, tile builds, validation runs, webhooks."
          actions={<><Btn>Subscribe ↗</Btn><Btn>Filters</Btn></>}
        />
        <Toolbar
          filters={<>
            <FiltChip>kind: all</FiltChip>
            <FiltChip on x>state: running, failed, partial</FiltChip>
            <FiltChip>range: last 24h</FiltChip>
            <FiltChip>resource: any</FiltChip>
            <FiltChip>actor: any</FiltChip>
            <FiltChip>+ filter</FiltChip>
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>auto-refresh</span>
            <Btn ghost sm>Pause</Btn>
            <Btn ghost sm>Export</Btn>
          </>}
        />
        <div style={{overflow:'auto',flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th style={{width:24}}></th>
              <th>State</th><th>Kind</th><th>Subject</th><th>Progress</th>
              <th>By</th><th>Started</th><th>Duration</th><th></th>
            </tr></thead>
            <tbody>
              <tr className="sel"><td>▸</td><td><Badge kind="warn">Running</Badge></td><td>Import</td><td><b>fire_obs.csv</b> → fire_observations</td><td style={{width:200}}><Bar pct={62} /></td><td className="mono">jamie</td><td className="muted">14:02</td><td className="mono">04:12</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge kind="warn">Running</Badge></td><td>Publish</td><td>parcels_2024 → OGC Features</td><td><Bar pct={88} /></td><td className="mono">jamie</td><td className="muted">14:05</td><td className="mono">00:48</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge kind="warn">Running</Badge></td><td>Refresh</td><td>wetlands_2025 from prod-postgis</td><td><Bar pct={12} /></td><td className="mono">system</td><td className="muted">14:06</td><td className="mono">00:18</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge>Queued</Badge></td><td>Tile build</td><td>parcels_2024 levels 8–14</td><td className="muted">in queue · 1</td><td className="mono">jamie</td><td className="muted">14:06</td><td className="mono">—</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge kind="ok">Done</Badge></td><td>Publish</td><td>obs_stations → Tile service</td><td className="muted">✓</td><td className="mono">system</td><td className="muted">13:52</td><td className="mono">00:38</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge kind="ok">Done</Badge></td><td>Validation</td><td>parcels_2024 (12 rules)</td><td className="muted">2 failed</td><td className="mono">system</td><td className="muted">13:48</td><td className="mono">00:04</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge kind="bad">Failed</Badge></td><td>Publish</td><td>parcels_2024 → Tile cache</td><td className="muted">at "warm cache" step</td><td className="mono">system</td><td className="muted">13:42</td><td className="mono">00:12</td><td><Btn sm>Retry</Btn></td></tr>
              <tr><td></td><td><Badge kind="warn">Partial</Badge></td><td>Import</td><td>Remote service "Parcels v3" → 3 resources</td><td className="muted">2 of 3 layers OK</td><td className="mono">k.tan</td><td className="muted">13:20</td><td className="mono">08:14</td><td><Btn sm>Open</Btn></td></tr>
              <tr><td></td><td><Badge kind="ok">Done</Badge></td><td>Refresh</td><td>roads_osm</td><td className="muted">✓ no changes</td><td className="mono">system</td><td className="muted">13:00</td><td className="mono">00:08</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge kind="ok">Done</Badge></td><td>Webhook</td><td>parcels published → analytics-bi</td><td className="muted">200 OK</td><td className="mono">system</td><td className="muted">12:55</td><td className="mono">00:00</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge kind="ok">Done</Badge></td><td>Connection sync</td><td>esri-online</td><td className="muted">✓ 38 items</td><td className="mono">system</td><td className="muted">12:00</td><td className="mono">00:14</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge>Canceled</Badge></td><td>Tile build</td><td>land_cover_2024 (manual cancel)</td><td className="muted">at level 12 / 14</td><td className="mono">jamie</td><td className="muted">10:14</td><td className="mono">22:08</td><td className="muted">⋯</td></tr>
              <tr><td></td><td><Badge kind="ok">Done</Badge></td><td>Audit</td><td>access policy snapshot</td><td className="muted">✓</td><td className="mono">system</td><td className="muted">09:00</td><td className="mono">00:01</td><td className="muted">⋯</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function ActivityJob() {
  return (
    <div className="scr">
      <TopBar crumbs={['Activity','Import #4821']} />
      <Sidebar active="activity" />
      <div className="main">
        <PageHead
          title="Import · fire_obs.csv → fire_observations"
          sub={<span><Badge kind="warn">Running · 62%</Badge> <span className="muted" style={{marginLeft:8}}>Started 14:02 by jamie · est. 4m remaining · #4821</span></span>}
          actions={<><Btn>Pause</Btn><Btn>Cancel</Btn></>}
        />
        <div style={{padding:'12px 18px',overflow:'auto',flex:1, display:'grid', gridTemplateColumns:'1fr 320px', gap:14}}>
          <div className="col">
            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
                <h3>Steps</h3>
                <div style={{flex:1}}/>
                <span className="muted" style={{fontSize:11}}>5 of 8</span>
              </div>
              <div className="col" style={{padding:'8px 12px',gap:6,fontSize:11.5}}>
                {[
                  { t:'Upload received', s:'ok', m:'248 MB · 14:02' },
                  { t:'Format detected · CSV with WKT', s:'ok', m:'14:02' },
                  { t:'Schema scanned', s:'ok', m:'9 columns, geom WKT' },
                  { t:'Destination prepared (Postgres)', s:'ok', m:'honua_imports.fire_observations' },
                  { t:'Loading rows · 1.3M of 2.1M', s:'run', m:'62% · 18.7k rows/s', pct:62 },
                  { t:'Build spatial index', s:'todo' },
                  { t:'Run validation', s:'todo' },
                  { t:'Create draft resource', s:'todo' },
                ].map((s,i) => (
                  <div key={i} className="row" style={{borderBottom:'1px dashed #eee', padding:'4px 0'}}>
                    <span style={{
                      width:14, textAlign:'center',
                      color: s.s === 'ok' ? 'var(--ok)' : s.s === 'run' ? 'var(--warn)' : s.s === 'bad' ? 'var(--bad)' : '#bbb'
                    }}>{s.s === 'ok' ? '✓' : s.s === 'run' ? '◐' : '○'}</span>
                    <span style={{flex:1, fontWeight: s.s === 'run' ? 600 : 400}}>{s.t}</span>
                    {s.m && <span className="muted mono" style={{fontSize:10}}>{s.m}</span>}
                  </div>
                ))}
                <Bar pct={62} />
              </div>
            </div>

            <div className="card" style={{padding:0, flex:1, display:'flex', flexDirection:'column'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
                <h3>Logs</h3>
                <div style={{flex:1}}/>
                <FiltChip>level: info+</FiltChip>
                <Btn ghost sm>Copy</Btn>
                <Btn ghost sm>Download</Btn>
              </div>
              <div className="log" style={{borderRadius:0, height:240}}>
                <div><span className="ts">14:02:01.012</span> <span className="info">[upload]</span> received 248 MB · sha256:7f3a…</div>
                <div><span className="ts">14:02:01.450</span> <span className="info">[scan]</span> detected CSV, 9 columns, sample 100 rows</div>
                <div><span className="ts">14:02:02.118</span> <span className="ok">[scan]</span> wkt column detected: <span className="mono">geom_wkt</span> · CRS guessed 4326</div>
                <div><span className="ts">14:02:02.218</span> <span className="info">[prep]</span> creating destination honua_imports.fire_observations</div>
                <div><span className="ts">14:02:03.001</span> <span className="ok">[prep]</span> table created with 9 columns + geom geometry(Point,4326)</div>
                <div><span className="ts">14:02:03.420</span> <span className="info">[load]</span> begin COPY from CSV · batches of 50k</div>
                <div><span className="ts">14:04:01.118</span> <span className="warn">[load]</span> 14 rows with null/invalid geometry — quarantined</div>
                <div><span className="ts">14:05:14.220</span> <span className="info">[load]</span> 1.3M / 2.1M rows · 18.7k r/s · ETA 04:12</div>
                <div><span className="ts">14:06:18.000</span> <span className="info">[load]</span> 1.6M / 2.1M rows · 19.2k r/s · ETA 02:51</div>
              </div>
            </div>
          </div>

          <div className="col">
            <div className="card">
              <h3>Inputs</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><span className="muted" style={{flex:1}}>File</span><span className="mono">fire_obs.csv</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Strategy</span><span>Copy into Honua</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Connection</span><span className="mono">prod-postgis</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Schema</span><span className="mono">honua_imports</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Target name</span><span className="mono">fire_observations</span></div>
              </div>
            </div>
            <div className="card">
              <h3>Outputs (preview)</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><span className="muted" style={{flex:1}}>Draft resource</span><span style={{color:'var(--pencil)'}}>◇ fire_observations</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Estimated features</span><span className="mono">2,100,000</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Quarantined rows</span><span className="mono">14</span></div>
              </div>
            </div>
            <Callout kind="warn"><b>14 rows quarantined</b> — invalid geometry. They'll be saved as a "rejected rows" file you can download from the job summary.</Callout>
          </div>
        </div>
      </div>
    </div>
  );
}

function ValidationCenter() {
  return (
    <div className="scr">
      <TopBar crumbs={['Validation']} />
      <Sidebar active="validation" />
      <div className="main">
        <PageHead
          title="Validation"
          sub="Readiness across every resource & target. Each row links to where you fix it."
          actions={<><Btn>Run all</Btn><Btn kind="p">Configure rules</Btn></>}
        />

        <div style={{padding:'10px 18px 0', display:'grid', gridTemplateColumns:'repeat(5,1fr)', gap:8}}>
          {[
            ['Ready', '108', 'ok'],
            ['Warning', '12', 'warn'],
            ['Blocked', '4', 'bad'],
            ['Not applicable', '36', 'mute'],
            ['Avg time to fix', '14m', 'info'],
          ].map((t,i) => (
            <div key={i} className="card" style={{padding:'8px 10px',gap:2}}>
              <div className="muted" style={{fontSize:10}}>{t[0].toUpperCase()}</div>
              <div className="row">
                <div style={{font:'600 18px var(--ui)'}}>{t[1]}</div>
                {t[2] === 'ok' && <Badge kind="ok">ready</Badge>}
                {t[2] === 'warn' && <Badge kind="warn">need fix</Badge>}
                {t[2] === 'bad' && <Badge kind="bad">blocks publish</Badge>}
              </div>
            </div>
          ))}
        </div>

        <Toolbar
          filters={<>
            <FiltChip on x>severity: blocked, warning</FiltChip>
            <FiltChip>target: any</FiltChip>
            <FiltChip>area: any</FiltChip>
            <FiltChip>resource: any</FiltChip>
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>16 of 160 issues shown</span>
            <Btn ghost sm>Group by target</Btn>
          </>}
        />

        <div style={{overflow:'auto',flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Severity</th><th>Resource</th><th>Target</th><th>Area</th><th>Issue</th><th>Fix location</th><th>Since</th>
            </tr></thead>
            <tbody>
              <tr><td><Badge kind="bad">Blocked</Badge></td><td><b>parcels_2024</b></td><td>OGC API Features</td><td>Schema</td><td>CRS not set on 2 features</td><td><span className="mono">Resource → Fields</span></td><td className="muted">14m</td></tr>
              <tr><td><Badge kind="bad">Blocked</Badge></td><td><b>watersheds_v3</b></td><td>GeoServices FeatureServer</td><td>Schema</td><td>Geometry not simple</td><td><span className="mono">Resource → Fields</span></td><td className="muted">2h</td></tr>
              <tr><td><Badge kind="bad">Blocked</Badge></td><td><b>fire_perimeters</b></td><td>WMTS</td><td>Cache</td><td>Tile pyramid levels not configured</td><td><span className="mono">Service → Cache</span></td><td className="muted">3h</td></tr>
              <tr><td><Badge kind="bad">Blocked</Badge></td><td><b>fire_observations</b></td><td>OGC API Features</td><td>Source</td><td>Source not yet imported</td><td><span className="mono">Activity → #4821</span></td><td className="muted">12m</td></tr>
              <tr><td><Badge kind="warn">Warning</Badge></td><td><b>parcels_2024</b></td><td>STAC</td><td>Metadata</td><td>Missing license URL</td><td><span className="mono">Resource → Metadata</span></td><td className="muted">1d</td></tr>
              <tr><td><Badge kind="warn">Warning</Badge></td><td><b>wetlands_2025</b></td><td>DCAT</td><td>Metadata</td><td>Missing publisher identifier</td><td><span className="mono">Resource → Metadata</span></td><td className="muted">2d</td></tr>
              <tr><td><Badge kind="warn">Warning</Badge></td><td><b>fire_perimeters</b></td><td>—</td><td>Source</td><td>Source schema drift detected (1 column added)</td><td><span className="mono">Resource → Source</span></td><td className="muted">28m</td></tr>
              <tr><td><Badge kind="warn">Warning</Badge></td><td><b>obs_stations</b></td><td>Esri catalog</td><td>Metadata</td><td>Missing thumbnail</td><td><span className="mono">Resource → Presentation</span></td><td className="muted">5d</td></tr>
              <tr><td><Badge kind="warn">Warning</Badge></td><td><b>monitoring_sites</b></td><td>—</td><td>Schema</td><td>14 rows with null geom</td><td><span className="mono">Resource → Fields</span></td><td className="muted">1h</td></tr>
              <tr><td><Badge kind="warn">Warning</Badge></td><td><b>land_cover_2024</b></td><td>ImageServer</td><td>Metadata</td><td>Raster metadata incomplete</td><td><span className="mono">Resource → Metadata</span></td><td className="muted">5d</td></tr>
              <tr><td><Badge kind="warn">Warning</Badge></td><td><b>census_blocks</b></td><td>OData</td><td>Security</td><td>Anonymous access on PII-adjacent field</td><td><span className="mono">Resource → Access</span></td><td className="muted">6d</td></tr>
              <tr><td><Badge kind="warn">Warning</Badge></td><td><b>roads_osm</b></td><td>OGC Records</td><td>Standards</td><td>Lineage statement under min length</td><td><span className="mono">Resource → Metadata</span></td><td className="muted">7d</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { Activity, ActivityJob, ValidationCenter });
