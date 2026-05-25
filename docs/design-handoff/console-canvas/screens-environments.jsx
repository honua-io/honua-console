// Environments, Fleet, Deploys, Alerts
//   EnvironmentsList   — overview of all envs, drift between, fleet health summary
//   EnvironmentDetail  — single env: services running, fleet sub-tab, version, recent promotions
//   DeployPromote      — promote canonical changes from env→env with diff + dry-run
//   AlertsList         — three filter scopes: env / runtime / definition

function EnvironmentsList() {
  const envs = [
    { k:'dev',     color:'#2a6fdb', state:'healthy',  fleet:'1 task',     version:'2.4.0-rc.3', changes:0, last:'12m', resources:128, services:9, alerts:0 },
    { k:'staging', color:'#d97706', state:'healthy',  fleet:'2 tasks',    version:'2.3.1',      changes:7, last:'2h',  resources:128, services:9, alerts:1 },
    { k:'prod',    color:'#1d6b3e', state:'degraded', fleet:'8 tasks · 1 unhealthy', version:'2.3.0', changes:14, last:'2d', resources:122, services:8, alerts:3 },
  ];

  return (
    <div className="scr">
      <TopBar crumbs={['Environments']} env="dev" />
      <Sidebar active="environments" />
      <div className="main">
        <PageHead
          title="Environments"
          sub={<span>The deployment fleet. <span className="muted">Canonical definitions (resources, metadata, styles) live above environments. Per-env state: connections credentials, runtime config, jobs, fleet health.</span></span>}
          actions={<>
            <Btn>Compare envs…</Btn>
            <Btn kind="p">Promote changes →</Btn>
          </>}
        />

        {/* pending promotions strip */}
        <div style={{padding:'8px 18px', background:'#fffae0', borderBottom:'1.2px solid #e7c97a', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="accent">7 canonical changes</Badge>
          <span style={{flex:1}}>
            <b>dev</b> is 7 changes ahead of <b>staging</b>; <b>staging</b> is 14 changes ahead of <b>prod</b>.
          </span>
          <Btn ghost sm>View changelog</Btn>
          <Btn sm>Dry-run promote dev → staging</Btn>
          <Btn kind="p" sm>Promote dev → staging</Btn>
        </div>

        <div style={{padding:'14px 18px', overflow:'auto', flex:1, display:'grid', gridTemplateColumns:'repeat(3, 1fr)', gap:14}}>
          {envs.map(e => (
            <div key={e.k} className="card" style={{padding:0}}>
              {/* header */}
              <div style={{padding:'10px 14px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center', gap:8}}>
                <span style={{width:10, height:10, borderRadius:'50%', background:e.color}} />
                <b style={{fontSize:14}}>{e.k}</b>
                {e.state === 'healthy'
                  ? <Badge kind="ok">healthy</Badge>
                  : <Badge kind="warn">degraded</Badge>}
                {e.alerts > 0 && <Badge kind="bad">{e.alerts} alert{e.alerts > 1 ? 's' : ''}</Badge>}
                <div style={{flex:1}}/>
                <Btn ghost sm>Switch to</Btn>
              </div>

              <div style={{padding:'10px 14px'}}>
                <dl className="kv" style={{fontSize:11.5}}>
                  <dt>Version</dt><dd className="mono">{e.version}</dd>
                  <dt>Fleet</dt><dd>{e.fleet}</dd>
                  <dt>Resources</dt><dd>{e.resources}</dd>
                  <dt>Services</dt><dd>{e.services}</dd>
                  <dt>Pending</dt><dd>{e.changes > 0 ? <span style={{color:'var(--warn)'}}><b>{e.changes}</b> changes behind</span> : <span style={{color:'var(--ok)'}}>up to date</span>}</dd>
                  <dt>Last promote</dt><dd className="muted">{e.last} ago</dd>
                </dl>
              </div>

              <div style={{padding:'6px 14px', borderTop:'1px dashed #eee', background:'#fafafa', display:'flex', gap:6}}>
                <Btn ghost sm>Activity</Btn>
                <Btn ghost sm>Fleet ↗</Btn>
                <div style={{flex:1}}/>
                {e.changes > 0 && <Btn sm>Promote →</Btn>}
              </div>
            </div>
          ))}
        </div>

        <div style={{padding:'10px 18px', borderTop:'1px solid #e4e4e4'}}>
          <div className="row" style={{marginBottom:6, fontSize:11}}>
            <h3 style={{margin:0}}>Drift across environments</h3>
            <span className="muted" style={{marginLeft:8}}>resources or settings that exist in some but not others</span>
            <div style={{flex:1}}/>
            <Btn ghost sm>Full comparison</Btn>
          </div>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Item</th><th>Kind</th><th>dev</th><th>staging</th><th>prod</th><th>Note</th>
            </tr></thead>
            <tbody>
              <tr><td className="mono">parcels_2024</td><td>resource</td><td><Badge kind="ok">v4</Badge></td><td><Badge>v3</Badge></td><td><Badge>v3</Badge></td><td className="muted">style change pending</td></tr>
              <tr><td className="mono">fire_observations</td><td>resource</td><td><Badge kind="ok">v1</Badge></td><td><Badge>—</Badge></td><td><Badge>—</Badge></td><td className="muted">new, never promoted</td></tr>
              <tr><td className="mono">monitoring_sites</td><td>resource</td><td><Badge kind="ok">v2</Badge></td><td><Badge kind="ok">v2</Badge></td><td><Badge>v1</Badge></td><td className="muted">staged for next prod promote</td></tr>
              <tr><td className="mono">parcels-fs</td><td>service</td><td><Badge kind="ok">live</Badge></td><td><Badge kind="ok">live</Badge></td><td><Badge>—</Badge></td><td className="muted">missing in prod</td></tr>
              <tr><td className="mono">cache TTL · features-public</td><td>service-runtime</td><td className="mono">5 min</td><td className="mono">30 min</td><td className="mono">30 min</td><td className="muted">per-env override</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function EnvironmentDetail() {
  return (
    <div className="scr">
      <TopBar crumbs={['Environments','prod']} env="prod" />
      <Sidebar active="environments" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Environments <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <span style={{width:12, height:12, borderRadius:'50%', background:'#1d6b3e'}} />
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>prod</h1>
            <Badge kind="warn" lg>degraded · 1 task</Badge>
            <Badge kind="bad">3 alerts</Badge>
            <span className="muted" style={{fontSize:11}}>v2.3.0 · us-west-2 · 14 changes behind staging</span>
            <div style={{flex:1}}/>
            <Btn>Promote staging → prod</Btn>
            <Btn ghost>Switch to prod</Btn>
          </div>
        </div>
        <Tabs items={[
          { k:'overview', t:'Overview' },
          { k:'fleet', t:'Fleet', ct:'8 tasks · 1 down' },
          { k:'connections', t:'Connections' },
          { k:'overrides', t:'Runtime overrides' },
          { k:'activity', t:'Activity' },
          { k:'history', t:'Promotion history' },
        ]} active="fleet" />

        {/* fleet sub-tab */}
        <Toolbar
          filters={<>
            <FiltChip on x>state: any</FiltChip>
            <FiltChip>role: any</FiltChip>
            <FiltChip>version: 2.3.0</FiltChip>
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>autoscaling group · target 8 · running 8</span>
            <Btn ghost sm>Scale…</Btn>
            <Btn ghost sm>Rolling restart</Btn>
          </>}
        />

        <div style={{overflow:'auto',flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Task</th><th>Role</th><th>Version</th><th>State</th><th>Uptime</th><th>p95 latency</th><th>RPS</th><th>CPU</th><th>Mem</th><th></th>
            </tr></thead>
            <tbody>
              {[
                ['prod-api-7f3a','api',     '2.3.0','ok',  '14d',  '184ms', '142', '38%','62%'],
                ['prod-api-b401','api',     '2.3.0','ok',  '14d',  '171ms', '128', '32%','58%'],
                ['prod-api-c9d2','api',     '2.3.0','warn','14d',  '742ms', '88',  '78%','81%'],
                ['prod-api-d815','api',     '2.3.0','ok',  '4d',   '162ms', '134', '34%','60%'],
                ['prod-tile-a01','tile',    '2.3.0','ok',  '14d',  '24ms',  '480', '22%','41%'],
                ['prod-tile-b14','tile',    '2.3.0','ok',  '14d',  '26ms',  '462', '24%','43%'],
                ['prod-job-e22','job-worker','2.3.0','ok', '7d',   '—',     '—',   '12%','38%'],
                ['prod-job-f88','job-worker','2.3.0','bad','—',    '—',     '—',   '—',  '—'],
              ].map((r,i) => (
                <tr key={i} style={r[3] === 'bad' ? {background:'#fbeae7'} : r[3] === 'warn' ? {background:'#fff7e6'} : null}>
                  <td className="mono">{r[0]}</td>
                  <td><span className="tag">{r[1]}</span></td>
                  <td className="mono">{r[2]}</td>
                  <td>
                    {r[3] === 'ok' && <Badge kind="ok">running</Badge>}
                    {r[3] === 'warn' && <Badge kind="warn">slow</Badge>}
                    {r[3] === 'bad' && <Badge kind="bad">unhealthy</Badge>}
                  </td>
                  <td className="muted mono">{r[4]}</td>
                  <td className="mono">{r[5]}</td>
                  <td className="mono">{r[6]}</td>
                  <td className="mono">{r[7]}</td>
                  <td className="mono">{r[8]}</td>
                  <td>
                    <div className="row" style={{gap:4, fontSize:10.5}}>
                      <a style={{cursor:'pointer'}}>Logs</a>
                      <span style={{color:'#ddd'}}>·</span>
                      <a style={{cursor:'pointer'}}>Drain</a>
                      <span style={{color:'#ddd'}}>·</span>
                      <a style={{cursor:'pointer'}}>Restart</a>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div style={{padding:'8px 18px', borderTop:'1.2px solid #e7a59c', background:'#fbeae7', display:'flex',alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="bad">1 task unhealthy</Badge>
          <span style={{flex:1}}>
            <b>prod-job-f88</b> has been restarting for 14m. Last log: <span className="mono">OOMKilled during tile build (parcels_2024 levels 12–14)</span>.
          </span>
          <Btn sm>View logs</Btn>
          <Btn kind="p" sm>Scale up memory</Btn>
        </div>
      </div>
    </div>
  );
}

function DeployPromote() {
  // Promotion flow: staging → prod
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Environments','Promote staging → prod']} env="staging" />
      <div className="wiz">
        <Stepper steps={['Review changes','Dry-run','Per-item options','Confirm','Apply']} on={1} />

        {/* env context */}
        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{width:10, height:10, borderRadius:'50%', background:'#d97706'}} />
          <b>staging</b>
          <span className="mono">v2.3.1</span>
          <span style={{color:'#bbb'}}>→</span>
          <span style={{width:10, height:10, borderRadius:'50%', background:'#1d6b3e'}} />
          <b>prod</b>
          <span className="mono">v2.3.0</span>
          <div style={{flex:1}}/>
          <Badge kind="accent">14 changes</Badge>
          <Badge kind="ok">dry-run passed</Badge>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, overflow:'auto'}}>
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Dry-run result</h2>
            <div className="muted" style={{fontSize:11.5, marginBottom:14}}>
              Honua simulated this promotion against prod's current state. No data has changed yet.
            </div>

            <div className="card" style={{padding:0, marginBottom:10}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:8, background:'#ecf7f0'}}>
                <span style={{color:'var(--ok)'}}>✓</span>
                <b style={{fontSize:11.5}}>Would succeed · 12 changes</b>
                <div style={{flex:1}}/>
                <a style={{fontSize:10.5,cursor:'pointer'}}>Expand</a>
              </div>
              <table className="tbl tbl--cmpt">
                <tbody>
                  <tr><td className="mono">parcels_2024</td><td>resource</td><td className="muted">v3 → v4</td><td><Badge kind="ok">no breaking change</Badge></td></tr>
                  <tr><td className="mono">monitoring_sites</td><td>resource</td><td className="muted">v1 → v2</td><td><Badge kind="ok">no breaking change</Badge></td></tr>
                  <tr><td className="mono">parcels-fs</td><td>service</td><td className="muted">create new</td><td><Badge kind="ok">compatible</Badge></td></tr>
                  <tr><td colSpan="4" className="muted" style={{textAlign:'center', padding:6, fontSize:10.5}}>+ 9 more</td></tr>
                </tbody>
              </table>
            </div>

            <div className="card" style={{padding:0, marginBottom:10}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:8, background:'#fff7e6'}}>
                <span style={{color:'var(--warn)'}}>⚠</span>
                <b style={{fontSize:11.5}}>Needs your attention · 2 items</b>
              </div>
              <div className="col" style={{padding:'8px 12px', gap:10, fontSize:11.5}}>
                <div>
                  <div className="row" style={{marginBottom:2}}>
                    <span className="mono"><b>features-public</b></span>
                    <span style={{color:'#bbb'}}>·</span>
                    <span>service runtime override in prod</span>
                  </div>
                  <div className="muted" style={{fontSize:10.5, marginBottom:4}}>
                    prod has a custom cache TTL of 30 min. Promotion would set it to 5 min (staging value). 
                  </div>
                  <div className="row" style={{gap:14, fontSize:11}}>
                    <label className="row" style={{gap:4}}><input type="radio" readOnly defaultChecked /> keep prod's 30 min</label>
                    <label className="row" style={{gap:4}}><input type="radio" readOnly /> overwrite with staging's 5 min</label>
                  </div>
                </div>
                <div>
                  <div className="row" style={{marginBottom:2}}>
                    <span className="mono"><b>fire_perimeters</b></span>
                    <span style={{color:'#bbb'}}>·</span>
                    <span>schema-breaking change</span>
                  </div>
                  <div className="muted" style={{fontSize:10.5, marginBottom:4}}>
                    Removes <span className="mono">cause_code</span> field (used by 1 prod consumer per usage analytics).
                  </div>
                  <div className="row" style={{gap:14, fontSize:11}}>
                    <label className="row" style={{gap:4}}><input type="radio" readOnly defaultChecked /> proceed (breaking)</label>
                    <label className="row" style={{gap:4}}><input type="radio" readOnly /> defer to next promote</label>
                  </div>
                </div>
              </div>
            </div>

            <div style={{padding:'8px 10px', border:'1px solid #e7a59c', borderRadius:6, background:'#fbeae7'}}>
              <div className="row" style={{marginBottom:4}}>
                <span style={{color:'var(--bad)'}}>✕</span>
                <b style={{fontSize:11.5}}>Would fail · 0 items</b>
                <div style={{flex:1}}/>
                <Badge kind="ok">clear</Badge>
              </div>
              <div className="muted" style={{fontSize:11}}>Nothing in this promotion would fail. Safe to apply once you've reviewed the 2 warnings.</div>
            </div>
          </div>

          <div className="col">
            <div className="card">
              <h3>Promotion bundle</h3>
              <dl className="kv">
                <dt>From</dt><dd className="mono">staging · v2.3.1</dd>
                <dt>To</dt><dd className="mono">prod · v2.3.0</dd>
                <dt>Items</dt><dd>14 changes</dd>
                <dt>Bundle ID</dt><dd className="mono" style={{fontSize:10}}>prom_01HZX7…d3f4</dd>
                <dt>Triggered by</dt><dd>jamie</dd>
              </dl>
            </div>

            <Callout kind="info">
              <b>Behind the scenes.</b> Honua commits this bundle to the config repo on apply. You can review the diff before commit, and rollbacks re-apply the previous bundle.
            </Callout>

            <div className="card">
              <h3>Apply strategy</h3>
              <div className="col" style={{gap:6, fontSize:11.5}}>
                <label className="row" style={{gap:6}}><input type="radio" readOnly defaultChecked /> Rolling · one task at a time</label>
                <label className="row" style={{gap:6}}><input type="radio" readOnly /> Blue-green · spin up parallel fleet</label>
                <label className="row" style={{gap:6}}><input type="radio" readOnly /> Immediate · all tasks at once (downtime)</label>
              </div>
              <div className="muted" style={{fontSize:10.5}}>Est. duration: 4 min · zero downtime</div>
            </div>

            <Ann red>after apply, you'll get an "auto-rollback if alert fires in 10m" window.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Review</Btn>
          <div className="row">
            <Btn ghost>Re-run dry-run</Btn>
            <Btn kind="p">Continue · Per-item options →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function AlertsList() {
  return (
    <div className="scr">
      <TopBar crumbs={['Alerts']} env="dev" />
      <Sidebar active="alerts" />
      <div className="main">
        <PageHead
          title="Alerts"
          sub="Things broken, things degrading, things drifting. Three scopes: environment / runtime / definition."
          actions={<>
            <Btn>Configure rules</Btn>
            <Btn>Subscribe ↗</Btn>
          </>}
        />

        <Toolbar
          filters={<>
            <FiltChip on x>scope: env, runtime, definition</FiltChip>
            <FiltChip on x>severity: critical, warning</FiltChip>
            <FiltChip>state: open</FiltChip>
            <FiltChip>env: any</FiltChip>
            <FiltChip>+ filter</FiltChip>
          </>}
          right={<>
            <span className="muted" style={{fontSize:11}}>4 open · auto-refresh 10s</span>
            <Btn ghost sm>Acknowledge all</Btn>
          </>}
        />

        <div style={{overflow:'auto',flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Severity</th><th>Scope</th><th>Env</th><th>Subject</th><th>Rule</th><th>Open for</th><th>Last fired</th><th style={{width:140}}>Actions</th>
            </tr></thead>
            <tbody>
              <tr style={{background:'#fbeae7'}}>
                <td><Badge kind="bad">critical</Badge></td>
                <td><span className="tag">env</span></td>
                <td><span className="row"><span style={{width:8,height:8,borderRadius:'50%',background:'#1d6b3e',marginRight:4}}/><b>prod</b></span></td>
                <td><b>1 task unhealthy</b> · prod-job-f88 OOMKilled</td>
                <td className="mono" style={{fontSize:10.5}}>fleet.task.unhealthy</td>
                <td className="mono">14m</td>
                <td className="muted">just now</td>
                <td><div className="row" style={{gap:4, fontSize:10.5}}><a style={{cursor:'pointer'}}>Logs</a> · <a style={{cursor:'pointer'}}>Page</a> · <a style={{cursor:'pointer'}}>Ack</a></div></td>
              </tr>
              <tr style={{background:'#fbeae7'}}>
                <td><Badge kind="bad">critical</Badge></td>
                <td><span className="tag">runtime</span></td>
                <td><b>prod</b></td>
                <td><b>Publish blocked</b> · parcels_2024 → features-public (CRS missing)</td>
                <td className="mono" style={{fontSize:10.5}}>validation.publish.blocked</td>
                <td className="mono">38m</td>
                <td className="muted">3m</td>
                <td><div className="row" style={{gap:4, fontSize:10.5}}><a style={{cursor:'pointer'}}>Open resource</a> · <a style={{cursor:'pointer'}}>Ack</a></div></td>
              </tr>
              <tr>
                <td><Badge kind="warn">warning</Badge></td>
                <td><span className="tag">definition</span></td>
                <td><b>staging</b></td>
                <td><b>License expires in 12 days</b></td>
                <td className="mono" style={{fontSize:10.5}}>license.expiry.warning</td>
                <td className="mono">1d</td>
                <td className="muted">1h</td>
                <td><div className="row" style={{gap:4, fontSize:10.5}}><a style={{cursor:'pointer'}}>Renew</a> · <a style={{cursor:'pointer'}}>Ack</a></div></td>
              </tr>
              <tr>
                <td><Badge kind="warn">warning</Badge></td>
                <td><span className="tag">runtime</span></td>
                <td><b>prod</b></td>
                <td><b>p95 latency degraded</b> · features-public · 742ms (3× baseline)</td>
                <td className="mono" style={{fontSize:10.5}}>service.latency.p95</td>
                <td className="mono">22m</td>
                <td className="muted">2m</td>
                <td><div className="row" style={{gap:4, fontSize:10.5}}><a style={{cursor:'pointer'}}>Open service</a> · <a style={{cursor:'pointer'}}>Ack</a></div></td>
              </tr>
              <tr style={{opacity:0.7}}>
                <td><Badge>info</Badge></td>
                <td><span className="tag">definition</span></td>
                <td><b>all</b></td>
                <td><b>Drift detected</b> · prod is 14 changes behind staging</td>
                <td className="mono" style={{fontSize:10.5}}>env.drift.behind</td>
                <td className="mono">2d</td>
                <td className="muted">2d</td>
                <td><div className="row" style={{gap:4, fontSize:10.5}}><a style={{cursor:'pointer'}}>Promote</a> · <a style={{cursor:'pointer'}}>Snooze</a></div></td>
              </tr>
              <tr style={{opacity:0.55}}>
                <td><Badge>info</Badge></td>
                <td><span className="tag">env</span></td>
                <td><b>prod</b></td>
                <td><b>Auto-scaled</b> · 6 tasks → 8 tasks (CPU)</td>
                <td className="mono" style={{fontSize:10.5}}>fleet.autoscale.up</td>
                <td className="mono">—</td>
                <td className="muted">5h</td>
                <td><div className="row" style={{gap:4, fontSize:10.5}}><a style={{cursor:'pointer'}}>View history</a></div></td>
              </tr>
            </tbody>
          </table>
        </div>

        {/* footer grouping legend */}
        <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', background:'#fafafa', fontSize:11, color:'#666', display:'flex', alignItems:'center', gap:14}}>
          <span style={{textTransform:'uppercase', fontSize:9.5, letterSpacing:'0.06em'}}>Scopes</span>
          <span><span className="tag">env</span> fleet, compute, autoscale</span>
          <span><span className="tag">runtime</span> service health, validation, publish</span>
          <span><span className="tag">definition</span> drift, license, version, audit</span>
          <div style={{flex:1}}/>
          <span className="muted">Rules &amp; routing in <a className="mono" style={{color:'var(--pencil)'}}>Settings → Alerting</a></span>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { EnvironmentsList, EnvironmentDetail, DeployPromote, AlertsList });
