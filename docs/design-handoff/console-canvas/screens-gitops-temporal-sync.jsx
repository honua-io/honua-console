// Honua Console · GitOps Release + Temporal Viewer + Sync Conflicts
// 5 screens:
//   GitOpsRelease       — semantic diff + env matrix + preflight + Git PR preview + CI timeline
//   GitOpsCITimeline    — drill-in: applied release with check timeline
//   TemporalViewer      — as-of slider + side-by-side diff + rollback
//   SyncConflictsList   — replicas with conflicted rows
//   SyncConflictReview  — base/client/server 3-way merge for one row

function GitOpsRelease() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Releases','rel_2104']} env="staging" area="operate" />
      <Sidebar active="environments" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Releases <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>rel_2104 · dev → staging</h1>
            <Badge>preflight</Badge>
            <span className="muted" style={{fontSize:11}}>7 semantic changes · bundle built 4m ago</span>
            <div style={{flex:1}}/>
            <Btn ghost>View on GitHub ↗</Btn>
            <Btn>Dry-run</Btn>
            <Btn kind="p">Open PR · proceed →</Btn>
          </div>
        </div>

        <Tabs items={[
          { k:'diff',     t:'Semantic diff', ct: 7 },
          { k:'matrix',   t:'Environment matrix' },
          { k:'preflight',t:'Compatibility preflight', ct: '2 warn' },
          { k:'scripts',  t:'Data scripts', ct: 3 },
          { k:'pr',       t:'Git PR preview' },
          { k:'ci',       t:'CI timeline' },
        ]} active="diff" />

        <div style={{display:'grid', gridTemplateColumns:'1fr 340px', flex:1, overflow:'hidden'}}>
          <div style={{overflow:'auto', padding:'12px 18px'}}>
            {/* SEMANTIC DIFF */}
            <div className="card" style={{padding:0, marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center'}}>
                <h3 style={{margin:0}}>Semantic diff · 7 changes</h3>
                <span className="muted" style={{fontSize:10.5, marginLeft:8}}>grouped by content type · click any row for raw JSON diff</span>
                <div style={{flex:1}}/>
                <FiltChip on x>impact: any</FiltChip>
                <FiltChip>changed by: any</FiltChip>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th></th><th>Item</th><th>Type</th><th>Change</th><th>Impact</th><th>Downstream</th><th>By</th>
                </tr></thead>
                <tbody>
                  <tr><td>◇</td><td><b>parcels_2024</b></td><td>resource</td><td>v3 → v4 · style + popup updated</td><td><Badge kind="ok">non-breaking</Badge></td><td>3 maps · 2 dashboards · 1 app</td><td className="mono">jamie</td></tr>
                  <tr><td>◇</td><td><b>fire_observations</b></td><td>resource</td><td>new · v1</td><td><Badge kind="ok">additive</Badge></td><td>—</td><td className="mono">jamie</td></tr>
                  <tr style={{background:'#fff7e6'}}><td>◇</td><td><b>fire_perimeters</b></td><td>resource</td><td>v2 → v3 · removed <span className="mono">cause_code</span> field</td><td><Badge kind="warn">breaking</Badge></td><td>1 dashboard (broken)</td><td className="mono">jamie</td></tr>
                  <tr><td>◐</td><td><b>parcels-use-map</b></td><td>map</td><td>v3 → v4 · style v3 → v4</td><td><Badge kind="ok">non-breaking</Badge></td><td>—</td><td className="mono">jamie</td></tr>
                  <tr><td>▤</td><td><b>public-works-fs</b></td><td>service</td><td>cache TTL 60m → 30m</td><td><Badge kind="ok">runtime</Badge></td><td>—</td><td className="mono">k.tan</td></tr>
                  <tr><td>▤</td><td><b>public-works-fs / layer 8</b></td><td>publication</td><td>new · binds fire_observations</td><td><Badge kind="ok">additive</Badge></td><td>—</td><td className="mono">jamie</td></tr>
                  <tr><td>⚿</td><td><b>access · partners</b></td><td>policy</td><td>added api-key scope: read:fire_obs</td><td><Badge kind="ok">additive</Badge></td><td>—</td><td className="mono">k.tan</td></tr>
                </tbody>
              </table>
            </div>

            {/* ENV MATRIX */}
            <div className="card" style={{padding:0, marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3 style={{margin:0}}>Environment matrix</h3>
                <div className="muted" style={{fontSize:10.5}}>which version of each item runs where</div>
              </div>
              <table className="matrix">
                <thead><tr>
                  <th className="row" style={{minWidth:200}}>Item</th>
                  <th>dev</th><th>staging</th><th>prod</th>
                </tr></thead>
                <tbody>
                  <tr><td className="row mono">parcels_2024</td><td><Badge kind="ok">v4</Badge></td><td><Badge>v3 → v4</Badge></td><td><Badge>v3</Badge></td></tr>
                  <tr><td className="row mono">fire_observations</td><td><Badge kind="ok">v1</Badge></td><td><Badge>— → v1</Badge></td><td><Badge>—</Badge></td></tr>
                  <tr style={{background:'#fff7e6'}}><td className="row mono">fire_perimeters</td><td><Badge kind="ok">v3</Badge></td><td><Badge kind="warn">v2 → v3</Badge></td><td><Badge>v2</Badge></td></tr>
                  <tr><td className="row mono">parcels-use-map</td><td><Badge kind="ok">v4</Badge></td><td><Badge>v3 → v4</Badge></td><td><Badge>v3</Badge></td></tr>
                  <tr><td className="row mono">public-works-fs · cache</td><td className="mono">30m</td><td className="mono">60m → 30m</td><td className="mono">60m</td></tr>
                </tbody>
              </table>
            </div>
          </div>

          {/* SIDE */}
          <div style={{borderLeft:'1px solid #e4e4e4', padding:'12px 14px', overflow:'auto', background:'#fafafa'}}>
            <Callout kind="warn">
              <b>1 breaking change.</b> fire_perimeters drops <span className="mono">cause_code</span>. One staging dashboard references it. Resolve before proceeding or defer.
            </Callout>

            <div className="card">
              <h3>Compatibility preflight</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><span style={{color:'var(--ok)', flex:1}}>✓ schema compatible</span><span className="muted">5 items</span></div>
                <div className="row"><span style={{color:'var(--warn)', flex:1}}>⚠ breaking · downstream affected</span><span className="muted">1 item</span></div>
                <div className="row"><span style={{color:'var(--warn)', flex:1}}>⚠ data script coverage gap</span><span className="muted">1 item</span></div>
                <div className="row"><span style={{color:'var(--ok)', flex:1}}>✓ access policy compatible</span><span className="muted">all</span></div>
                <div className="row"><span style={{color:'var(--ok)', flex:1}}>✓ runtime constraints met</span><span className="muted">all</span></div>
              </div>
            </div>

            <div className="card">
              <h3>Data scripts · 3</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><span className="mono" style={{flex:1, fontSize:10}}>001_add_fire_obs_index.sql</span><Badge kind="ok">covered</Badge></div>
                <div className="row"><span className="mono" style={{flex:1, fontSize:10}}>002_drop_cause_code.sql</span><Badge kind="warn">no rollback</Badge></div>
                <div className="row"><span className="mono" style={{flex:1, fontSize:10}}>003_grant_fire_obs.sql</span><Badge kind="ok">covered</Badge></div>
              </div>
              <Btn ghost sm>View scripts ↗</Btn>
            </div>

            <div className="card">
              <h3>What proceed does</h3>
              <ol style={{margin:'4px 0 0 16px', padding:0, fontSize:11, lineHeight:1.6}}>
                <li>Open Git PR on <span className="mono">honua-config</span></li>
                <li>CI runs preflight + dry-run on staging</li>
                <li>On green, await human approval</li>
                <li>Apply with chosen strategy (rolling default)</li>
                <li>Auto-rollback window: 10m</li>
              </ol>
            </div>

            <Ann red>governed operation · emits audit + creates release artifact.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function GitOpsCITimeline() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Releases','rel_2099','CI timeline']} env="staging" area="operate" />
      <Sidebar active="environments" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Releases <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>rel_2099 · dev → staging</h1>
            <Badge kind="ok" lg>applied · 12h ago</Badge>
            <span className="muted" style={{fontSize:11}}>7 changes · zero downtime · 4m 12s</span>
            <div style={{flex:1}}/>
            <Btn ghost>View PR ↗</Btn>
            <Btn ghost>View bundle</Btn>
            <Btn>Rollback…</Btn>
          </div>
        </div>

        <Tabs items={[
          { k:'diff', t:'Semantic diff' },
          { k:'matrix', t:'Environment matrix' },
          { k:'preflight', t:'Preflight' },
          { k:'scripts', t:'Data scripts' },
          { k:'pr', t:'Git PR' },
          { k:'ci', t:'CI timeline' },
        ]} active="ci" />

        <div style={{display:'grid', gridTemplateColumns:'1fr 340px', flex:1, overflow:'hidden'}}>
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3 style={{margin:0}}>Check timeline</h3>
                <div className="muted" style={{fontSize:10.5}}>chronological · each row links to logs</div>
              </div>
              <div style={{padding:'12px 14px', display:'flex', flexDirection:'column', gap:6, fontSize:11.5}}>
                {[
                  { t:'12h ago', st:'ok', n:'PR opened by jamie', d:'rel_2099 · honua-config#412', tag:'git' },
                  { t:'12h ago', st:'ok', n:'Preflight · schema compatibility', d:'7 items · 5 non-breaking · 2 additive', tag:'check' },
                  { t:'12h ago', st:'ok', n:'Preflight · access policy', d:'no policy regressions', tag:'check' },
                  { t:'12h ago', st:'ok', n:'Preflight · data script coverage', d:'3 scripts · all covered', tag:'check' },
                  { t:'12h ago', st:'ok', n:'Dry-run · staging', d:'42 simulated operations · all green', tag:'dryrun' },
                  { t:'12h ago', st:'ok', n:'Human approval', d:'approved by k.tan', tag:'approval' },
                  { t:'12h ago', st:'ok', n:'Lock acquired · staging', d:'release lock · 60s timeout', tag:'lock' },
                  { t:'12h ago', st:'ok', n:'Data scripts · staging', d:'001 · 002 · 003 applied in 18s', tag:'data' },
                  { t:'12h ago', st:'ok', n:'Config commit · honua-config@a3bf021', d:'7 items written', tag:'commit' },
                  { t:'12h ago', st:'ok', n:'Rolling deploy · 4 tasks', d:'1 at a time · 60s drain · 4m 12s total', tag:'deploy' },
                  { t:'12h ago', st:'ok', n:'Smoke tests', d:'4 passed · features-public · catalogs · tile-cache · auth', tag:'check' },
                  { t:'12h ago', st:'ok', n:'Auto-rollback window opened', d:'10m · would trigger on critical alert', tag:'window' },
                  { t:'11h 50m ago', st:'ok', n:'Auto-rollback window closed', d:'no alerts fired · release stable', tag:'window' },
                  { t:'11h 50m ago', st:'ok', n:'Audit event emitted', d:'evt_91a4 · release.applied', tag:'audit' },
                ].map((s,i) => (
                  <div key={i} className="row" style={{padding:'5px 8px', borderLeft:'2px solid var(--ok)', background:'#fafdf6', borderRadius:'0 4px 4px 0'}}>
                    <span style={{color:'var(--ok)', width:18, textAlign:'center'}}>✓</span>
                    <div style={{flex:1}}>
                      <div style={{fontWeight:600, fontSize:11.5}}>{s.n}</div>
                      <div className="muted" style={{fontSize:10.5}}>{s.d}</div>
                    </div>
                    <span className="tag">{s.tag}</span>
                    <span className="muted mono" style={{fontSize:10}}>{s.t}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* SIDE */}
          <div style={{borderLeft:'1px solid #e4e4e4', padding:'14px 14px', overflow:'auto', background:'#fafafa'}}>
            <div className="card">
              <h3>Release</h3>
              <dl className="kv">
                <dt>ID</dt><dd className="mono">rel_2099</dd>
                <dt>From</dt><dd>dev · v2.4.0-rc.3</dd>
                <dt>To</dt><dd>staging · v2.3.1</dd>
                <dt>PR</dt><dd className="mono">honua-config#412</dd>
                <dt>Commit</dt><dd className="mono">a3bf021</dd>
                <dt>Author</dt><dd>jamie</dd>
                <dt>Approver</dt><dd>k.tan</dd>
                <dt>Duration</dt><dd>4m 12s</dd>
                <dt>Strategy</dt><dd>rolling</dd>
              </dl>
            </div>

            <Callout kind="info">
              <b>Rollback re-applies bundle for rel_2098.</b> Available for 14 days from this release timestamp.
            </Callout>

            <div className="card">
              <h3>Post-release alerts</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><span style={{flex:1}}>p95 latency drift</span><Badge>none</Badge></div>
                <div className="row"><span style={{flex:1}}>Error rate</span><Badge kind="ok">baseline</Badge></div>
                <div className="row"><span style={{flex:1}}>Validation failures</span><Badge>0</Badge></div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function TemporalViewer() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Temporal viewer','parcels_2024']} env="prod" area="operate" />
      <Sidebar active="resources" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Temporal viewer <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <span style={{color:'var(--pencil)',fontSize:18}}>◇</span>
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>parcels_2024 · as-of</h1>
            <Badge kind="ok">temporal-enabled</Badge>
            <span className="muted" style={{fontSize:11}}>1.28M features · valid-time tracking on · history 90d</span>
            <div style={{flex:1}}/>
            <Btn ghost>Open in Studio →</Btn>
            <Btn>Diff selected snapshots</Btn>
            <Btn kind="p">Rollback to selected…</Btn>
          </div>
        </div>

        {/* Timeline scrubber */}
        <div style={{padding:'12px 18px', borderBottom:'1px solid #e4e4e4', background:'#fafafa'}}>
          <div className="row" style={{marginBottom:6, fontSize:11}}>
            <span className="muted" style={{textTransform:'uppercase', fontSize:9.5, letterSpacing:'0.06em', flex:1}}>As-of time</span>
            <span className="mono">2026-05-14 12:00 UTC</span>
          </div>
          <div style={{position:'relative', height:46, background:'#fff', border:'1px solid #d8d8d8', borderRadius:5, marginBottom:6}}>
            {/* axis */}
            <div style={{position:'absolute', top:24, left:8, right:8, height:2, background:'#ddd'}}/>
            {/* edits as ticks */}
            {[8, 14, 18, 23, 38, 46, 58, 66, 74, 78, 85, 91].map((p,i) => (
              <div key={i} style={{
                position:'absolute', top:18, left:`calc(${p}% + 8px)`,
                width:2, height:14, background:'#7a6f55',
              }}/>
            ))}
            {/* releases as bigger ticks */}
            {[24, 52, 80].map((p,i) => (
              <div key={'r'+i} style={{
                position:'absolute', top:12, left:`calc(${p}% + 8px)`,
                width:3, height:24, background:'var(--accent-deep)',
              }} title={'release ' + (i+1)}/>
            ))}
            {/* current playhead */}
            <div style={{
              position:'absolute', top:6, left:'58%', width:0, height:34,
              borderLeft:'2px solid var(--ink)',
            }}/>
            <div style={{
              position:'absolute', top:0, left:'calc(58% - 30px)', padding:'2px 6px',
              background:'var(--ink)', color:'#fff', fontSize:9.5, borderRadius:3,
              fontFamily:'var(--mono)',
            }}>14 May · 12:00</div>
            {/* axis labels */}
            <div style={{position:'absolute', bottom:2, left:8, fontSize:9.5, color:'#888'}}>30 days ago</div>
            <div style={{position:'absolute', bottom:2, right:8, fontSize:9.5, color:'#888'}}>now</div>
          </div>
          <div className="row" style={{fontSize:10.5}}>
            <span className="row" style={{gap:6,marginRight:14}}><span style={{width:8,height:8,background:'#7a6f55'}}/>edit · 12 in range</span>
            <span className="row" style={{gap:6,marginRight:14}}><span style={{width:8,height:8,background:'var(--accent-deep)'}}/>release · 3 in range</span>
            <div style={{flex:1}}/>
            <Btn ghost sm>Jump to release ▾</Btn>
            <Btn ghost sm>Latest</Btn>
          </div>
        </div>

        {/* split: as-of view vs current */}
        <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', flex:1, overflow:'hidden'}}>
          {/* AS-OF */}
          <div style={{display:'flex', flexDirection:'column', borderRight:'1px solid #e4e4e4'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', display:'flex',alignItems:'center', gap:8}}>
              <Badge>as-of</Badge>
              <span className="mono" style={{fontSize:11}}>2026-05-14 12:00 UTC · 10d ago</span>
              <div style={{flex:1}}/>
              <span className="muted" style={{fontSize:10.5}}>1,283,914 features</span>
            </div>
            <div style={{padding:10, background:'#fafafa', borderBottom:'1px solid #e4e4e4'}}>
              <div style={{filter:'saturate(0.7)'}}>
                <MapPreview mode="layer" height={220} popup={false} scaleText="1:8,000" />
              </div>
            </div>
            <div style={{padding:'8px 12px', flex:1, overflow:'auto'}}>
              <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>Sample features at this time</div>
              <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
                <thead><tr><th>gid</th><th>parcel_id</th><th>use_code</th><th>assessed</th></tr></thead>
                <tbody>
                  <tr><td>1</td><td className="mono">04-021-118</td><td className="mono">R-1</td><td className="num mono">$542,000</td></tr>
                  <tr><td>2</td><td className="mono">04-021-119</td><td className="mono">R-1</td><td className="num mono">$498,000</td></tr>
                  <tr><td>3</td><td className="mono">04-021-120</td><td className="mono">C-2</td><td className="num mono">$1,820,000</td></tr>
                  <tr style={{background:'#fff7e6'}}><td>4</td><td className="mono">04-021-121</td><td className="mono">R-1</td><td className="num mono">$502,000</td></tr>
                </tbody>
              </table>
            </div>
          </div>

          {/* NOW */}
          <div style={{display:'flex', flexDirection:'column'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fffae0', display:'flex',alignItems:'center', gap:8}}>
              <Badge kind="ok">now</Badge>
              <span className="mono" style={{fontSize:11}}>2026-05-23 18:42 UTC · current</span>
              <div style={{flex:1}}/>
              <span className="muted" style={{fontSize:10.5}}>1,284,021 features</span>
            </div>
            <div style={{padding:10}}>
              <MapPreview mode="layer" height={220} popup={false} scaleText="1:8,000" />
            </div>
            <div style={{padding:'8px 12px', flex:1, overflow:'auto'}}>
              <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>What changed</div>
              <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
                <thead><tr><th>Change</th><th>Count</th><th>Last</th></tr></thead>
                <tbody>
                  <tr><td>features added</td><td className="num mono">107</td><td className="muted">3h ago</td></tr>
                  <tr><td>features modified</td><td className="num mono">2,418</td><td className="muted">2h ago</td></tr>
                  <tr><td>features deleted</td><td className="num mono">82</td><td className="muted">1d ago</td></tr>
                  <tr style={{background:'#fff7e6'}}><td>assessed_value changed (sample)</td><td className="num mono" colSpan={2}>04-021-121 · $502,000 → $582,000</td></tr>
                </tbody>
              </table>
              <Callout kind="info" style={{marginTop:8}}>
                <b>Rollback to as-of would affect 2,607 features.</b> Honua quarantines current values into a recovery snapshot before applying.
              </Callout>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function SyncConflictsList() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Sync conflicts']} env="prod" area="operate" />
      <Sidebar active="activity" />
      <div className="main">
        <PageHead
          title="Sync conflicts"
          sub="Disconnected replicas reconnecting · conflicted rows surfaced before they hit canonical."
          actions={<>
            <Btn>Subscribe ↗</Btn>
            <Btn ghost>Rules</Btn>
          </>}
        />

        <Toolbar
          filters={<>
            <FiltChip on x>state: open</FiltChip>
            <FiltChip>replica: any</FiltChip>
            <FiltChip>resource: any</FiltChip>
            <FiltChip>conflict type: any</FiltChip>
          </>}
          right={<span className="muted" style={{fontSize:11}}>4 replicas with conflicts · 38 rows</span>}
        />

        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Replica</th>
              <th>Resource</th>
              <th>Disconnected for</th>
              <th>Conflicts</th>
              <th>Auto-resolved</th>
              <th>Needs review</th>
              <th>Last sync attempt</th>
              <th></th>
            </tr></thead>
            <tbody>
              <tr style={{background:'#fff7e6'}}>
                <td><span style={{color:'#666'}}>📱</span> <b>field-tablet-3</b></td>
                <td className="mono">parcels_2024</td>
                <td>4d 14h</td>
                <td className="num mono">14</td>
                <td className="num mono">8</td>
                <td><Badge kind="warn">6</Badge></td>
                <td className="muted">2m ago</td>
                <td><Btn sm>Review →</Btn></td>
              </tr>
              <tr style={{background:'#fff7e6'}}>
                <td><span style={{color:'#666'}}>📱</span> <b>field-tablet-7</b></td>
                <td className="mono">hydrants_2024</td>
                <td>1d 02h</td>
                <td className="num mono">9</td>
                <td className="num mono">5</td>
                <td><Badge kind="warn">4</Badge></td>
                <td className="muted">14m ago</td>
                <td><Btn sm>Review</Btn></td>
              </tr>
              <tr>
                <td><span style={{color:'#666'}}>📱</span> field-laptop-1</td>
                <td className="mono">monitoring_sites</td>
                <td>6h 22m</td>
                <td className="num mono">8</td>
                <td className="num mono">8</td>
                <td><Badge kind="ok">0</Badge></td>
                <td className="muted">38m ago</td>
                <td><Btn ghost sm>Open</Btn></td>
              </tr>
              <tr style={{background:'#fff7e6'}}>
                <td><span style={{color:'#666'}}>📱</span> <b>survey-rig-A</b></td>
                <td className="mono">obs_stations</td>
                <td>12d 04h</td>
                <td className="num mono">7</td>
                <td className="num mono">2</td>
                <td><Badge kind="warn">5</Badge></td>
                <td className="muted">3h ago</td>
                <td><Btn sm>Review</Btn></td>
              </tr>
            </tbody>
          </table>
        </div>

        <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', background:'#fafafa', display:'flex', alignItems:'center', gap:8, fontSize:11}}>
          <span className="muted">Auto-resolution rules:</span>
          <span className="tag">latest-wins on identical fields</span>
          <span className="tag">spatial: snap if ≤ 1m delta</span>
          <span className="tag">temporal: server wins past final-published</span>
          <div style={{flex:1}}/>
          <Btn ghost sm>Edit rules</Btn>
        </div>
      </div>
    </div>
  );
}

function SyncConflictReview() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Sync conflicts','field-tablet-3','Row 04-021-204']} env="prod" area="operate" />
      <Sidebar active="activity" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Sync conflicts <span style={{color:'#bbb'}}>/</span> field-tablet-3 <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>Conflict · parcel 04-021-204</h1>
            <Badge kind="warn">3-way merge</Badge>
            <span className="muted" style={{fontSize:11}}>row 1 of 6 needing review · replica disconnected 4d 14h</span>
            <div style={{flex:1}}/>
            <Btn ghost>← Previous</Btn>
            <Btn ghost>Skip · review later</Btn>
            <Btn>Next →</Btn>
          </div>
        </div>

        {/* 3-way merge */}
        <div style={{display:'grid', gridTemplateColumns:'1fr 1fr 1fr', flex:1, overflow:'hidden'}}>
          {/* BASE */}
          <div style={{borderRight:'1px solid #e4e4e4', overflow:'auto', background:'#fafafa'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
              <Badge>base · 9 May 14:00</Badge>
              <div className="muted" style={{fontSize:10.5, marginTop:4}}>state when client last synced</div>
            </div>
            <table className="tbl tbl--cmpt" style={{fontSize:11}}>
              <tbody>
                <tr><td className="muted">parcel_id</td><td className="mono">04-021-204</td></tr>
                <tr><td className="muted">use_code</td><td className="mono">R-1</td></tr>
                <tr><td className="muted">area_m2</td><td className="mono">2,008</td></tr>
                <tr><td className="muted">assessed_value</td><td className="mono">$502,000</td></tr>
                <tr><td className="muted">last_assessment</td><td className="mono">2024-08-12</td></tr>
                <tr><td className="muted">geom (sample)</td><td className="mono" style={{fontSize:10}}>POLYGON((-122.1 …))</td></tr>
              </tbody>
            </table>
            <div style={{padding:'10px 12px'}}>
              <div className="muted" style={{fontSize:10.5, marginBottom:4}}>map at base</div>
              <div style={{filter:'saturate(0.6) opacity(0.85)'}}>
                <MapPreview mode="layer" height={140} popup={false} scaleText="1:2,000" />
              </div>
            </div>
          </div>

          {/* CLIENT */}
          <div style={{borderRight:'1px solid #e4e4e4', overflow:'auto', background:'#fff'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:6}}>
              <Badge kind="info">client</Badge>
              <span className="muted" style={{fontSize:10.5}}>field-tablet-3 · edited 11 May 09:24</span>
            </div>
            <table className="tbl tbl--cmpt" style={{fontSize:11}}>
              <tbody>
                <tr><td className="muted">parcel_id</td><td className="mono">04-021-204</td></tr>
                <tr><td className="muted">use_code</td><td className="mono">R-1</td></tr>
                <tr><td className="muted">area_m2</td><td className="mono">2,008</td></tr>
                <tr style={{background:'#eaf0fb'}}><td className="muted">assessed_value</td><td className="mono"><b>$558,000</b> ← client</td></tr>
                <tr style={{background:'#eaf0fb'}}><td className="muted">last_assessment</td><td className="mono"><b>2024-09-30</b> ← client</td></tr>
                <tr><td className="muted">geom (sample)</td><td className="mono" style={{fontSize:10}}>POLYGON((-122.1 …))</td></tr>
              </tbody>
            </table>
            <div style={{padding:'10px 12px'}}>
              <div className="muted" style={{fontSize:10.5, marginBottom:4}}>client edits</div>
              <div className="col" style={{gap:3, fontSize:11}}>
                <div className="row"><Badge kind="info">edit</Badge><span style={{marginLeft:6}}>assessed_value <span className="mono">$502k → $558k</span></span></div>
                <div className="row"><Badge kind="info">edit</Badge><span style={{marginLeft:6}}>last_assessment <span className="mono">2024-08-12 → 2024-09-30</span></span></div>
                <div className="muted" style={{fontSize:10.5, marginTop:4}}>signed by k.tan · offline edit · GPS within 0.5m</div>
              </div>
            </div>
            <div style={{padding:'8px 12px', borderTop:'1px dashed #e4e4e4'}}>
              <label className="row" style={{gap:6, fontSize:11.5, cursor:'pointer'}}>
                <input type="radio" name="winner" readOnly />
                <span>Take client values</span>
              </label>
            </div>
          </div>

          {/* SERVER */}
          <div style={{overflow:'auto', background:'#fff'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:6}}>
              <Badge kind="accent">server</Badge>
              <span className="muted" style={{fontSize:10.5}}>canonical · edited 12 May 16:08 by jamie</span>
            </div>
            <table className="tbl tbl--cmpt" style={{fontSize:11}}>
              <tbody>
                <tr><td className="muted">parcel_id</td><td className="mono">04-021-204</td></tr>
                <tr><td className="muted">use_code</td><td className="mono">R-1</td></tr>
                <tr><td className="muted">area_m2</td><td className="mono">2,008</td></tr>
                <tr style={{background:'#fffae0'}}><td className="muted">assessed_value</td><td className="mono"><b>$582,000</b> ← server</td></tr>
                <tr style={{background:'#fffae0'}}><td className="muted">last_assessment</td><td className="mono"><b>2024-10-04</b> ← server</td></tr>
                <tr><td className="muted">geom (sample)</td><td className="mono" style={{fontSize:10}}>POLYGON((-122.1 …))</td></tr>
              </tbody>
            </table>
            <div style={{padding:'10px 12px'}}>
              <div className="muted" style={{fontSize:10.5, marginBottom:4}}>server edits</div>
              <div className="col" style={{gap:3, fontSize:11}}>
                <div className="row"><Badge kind="accent">edit</Badge><span style={{marginLeft:6}}>assessed_value <span className="mono">$502k → $582k</span></span></div>
                <div className="row"><Badge kind="accent">edit</Badge><span style={{marginLeft:6}}>last_assessment <span className="mono">2024-08-12 → 2024-10-04</span></span></div>
                <div className="muted" style={{fontSize:10.5, marginTop:4}}>from canonical · v4 publish</div>
              </div>
            </div>
            <div style={{padding:'8px 12px', borderTop:'1px dashed #e4e4e4'}}>
              <label className="row" style={{gap:6, fontSize:11.5, cursor:'pointer'}}>
                <input type="radio" name="winner" readOnly defaultChecked />
                <span>Keep server values (recommended · newer)</span>
              </label>
            </div>
          </div>
        </div>

        {/* AI advisory + actions */}
        <div style={{borderTop:'1.2px solid var(--accent-deep)', background:'#fffdf3', padding:'10px 18px', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{fontSize:14}}>🤖</span>
          <div style={{flex:1}}>
            <b>Server values are newer for both fields</b> (server 12 May vs client 11 May). Server also matches the most recent assessment cycle. Recommended: keep server, archive client's edit as evidence.
          </div>
          <Btn ghost sm>Take client (override)</Btn>
          <Btn sm>Merge per-field…</Btn>
          <Btn kind="p" sm>Keep server &amp; archive client</Btn>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { GitOpsRelease, GitOpsCITimeline, TemporalViewer, SyncConflictsList, SyncConflictReview });
