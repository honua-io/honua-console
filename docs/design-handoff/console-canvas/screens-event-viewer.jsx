// Honua Console · Event Viewer + Investigation
// Absorbs old "Activity" and "Alerts" feeds into one unified timeline filterable
// by every dimension in the doc. Drawer for one event with raw evidence + related objects.
//
// 3 screens:
//   EventViewerList   — dense timeline/table with comprehensive filters
//   EventDetailDrawer — single event detail + related objects + AI advisory
//   Investigation     — pinning events to an investigation with notes

function EventViewerList() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Event viewer']} env="prod" area="operate" />
      <Sidebar active="activity" />
      <div className="main">
        <PageHead
          title="Event viewer"
          sub="Unified timeline · logs · audit · jobs · alerts · releases · sync · data changes. Filter, pin, investigate."
          actions={<>
            <Btn>Subscribe ↗</Btn>
            <Btn ghost>Saved views ▾</Btn>
            <Btn kind="p">+ Investigation</Btn>
          </>}
        />

        {/* Multi-line filter builder */}
        <div style={{padding:'8px 18px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', display:'flex', alignItems:'center', gap:6, flexWrap:'wrap', fontSize:11}}>
          <span className="muted" style={{textTransform:'uppercase', fontSize:9.5, letterSpacing:'0.06em'}}>Filters</span>
          <FiltChip on x>env: prod, staging</FiltChip>
          <FiltChip on x>severity: ≥ warning</FiltChip>
          <FiltChip on x>last 24h</FiltChip>
          <FiltChip>type: alert, job, release, sync, audit, data, log</FiltChip>
          <FiltChip>resource: any</FiltChip>
          <FiltChip>actor: any</FiltChip>
          <FiltChip>trace ID</FiltChip>
          <FiltChip>request ID</FiltChip>
          <FiltChip>job ID</FiltChip>
          <FiltChip>release ID</FiltChip>
          <FiltChip>replica ID</FiltChip>
          <FiltChip>change set ID</FiltChip>
          <FiltChip>+</FiltChip>
          <div style={{flex:1}}/>
          <Btn ghost sm>Save view</Btn>
          <Btn ghost sm>Export</Btn>
        </div>

        {/* Type strip */}
        <div style={{padding:'6px 18px', borderBottom:'1px solid #e4e4e4', background:'#fff', display:'flex', alignItems:'center', gap:6, fontSize:11}}>
          <span className="muted" style={{textTransform:'uppercase', fontSize:9.5, letterSpacing:'0.06em', marginRight:4}}>Types</span>
          {[
            ['alert','12','bad'],
            ['job','84','ok'],
            ['release','3','accent'],
            ['sync','7','info'],
            ['audit','42','mute'],
            ['data','118','mute'],
            ['log','842','mute'],
          ].map(([t,n,k]) => (
            <span key={t} className="row" style={{gap:3, padding:'2px 8px', borderRadius:11, border:'1px solid #d8d8d8', background:'#fff', cursor:'pointer'}}>
              <span className="tag">{t}</span><span className="muted mono" style={{fontSize:10}}>{n}</span>
            </span>
          ))}
          <div style={{flex:1}}/>
          <span className="muted" style={{fontSize:10.5}}>auto-refresh · 5s · paused</span>
        </div>

        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt" style={{fontSize:11}}>
            <thead><tr>
              <th style={{width:24}}></th>
              <th style={{width:90}}>Time</th>
              <th style={{width:70}}>Severity</th>
              <th style={{width:60}}>Type</th>
              <th style={{width:60}}>Env</th>
              <th>Message</th>
              <th>Resource / target</th>
              <th>Actor</th>
              <th style={{width:100}}>Correlation</th>
            </tr></thead>
            <tbody>
              <tr style={{background:'#fbeae7'}}>
                <td>📌</td>
                <td className="mono">14:14:22</td>
                <td><Badge kind="bad">critical</Badge></td>
                <td><span className="tag">alert</span></td>
                <td className="mono">prod</td>
                <td><b>Task unhealthy</b> · OOMKilled during tile build (parcels_2024 levels 12–14)</td>
                <td className="mono">prod-job-f88</td>
                <td className="mono">system</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>job_4821</td>
              </tr>
              <tr style={{background:'#fff'}}>
                <td></td>
                <td className="mono">14:14:18</td>
                <td><Badge kind="bad">error</Badge></td>
                <td><span className="tag">log</span></td>
                <td className="mono">prod</td>
                <td><span className="mono" style={{fontSize:10}}>level=error msg="memory limit exceeded" mem_used=1.84GB limit=2GB</span></td>
                <td className="mono">prod-job-f88</td>
                <td className="mono">system</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>job_4821</td>
              </tr>
              <tr style={{background:'#fff7e6'}}>
                <td></td>
                <td className="mono">14:06:10</td>
                <td><Badge kind="warn">warning</Badge></td>
                <td><span className="tag">alert</span></td>
                <td className="mono">prod</td>
                <td>p95 latency degraded · features-public · 742ms (3× baseline)</td>
                <td className="mono">features-public</td>
                <td className="mono">system</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>slo_142</td>
              </tr>
              <tr>
                <td></td>
                <td className="mono">14:05:42</td>
                <td><Badge>info</Badge></td>
                <td><span className="tag">job</span></td>
                <td className="mono">dev</td>
                <td><b>Publish completed</b> · parcels-use-map v4 · 4 layers · 0 warnings</td>
                <td className="mono">parcels-use-map</td>
                <td className="mono">jamie</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>job_4820</td>
              </tr>
              <tr>
                <td></td>
                <td className="mono">14:05:38</td>
                <td><Badge>info</Badge></td>
                <td><span className="tag">audit</span></td>
                <td className="mono">dev</td>
                <td>Content version created · <span className="mono">parcels-use-map v4</span></td>
                <td className="mono">parcels-use-map</td>
                <td className="mono">jamie</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>req_7f3a</td>
              </tr>
              <tr>
                <td></td>
                <td className="mono">14:02:01</td>
                <td><Badge>info</Badge></td>
                <td><span className="tag">job</span></td>
                <td className="mono">dev</td>
                <td><b>Import started</b> · fire_obs.csv → fire_observations · 2.1M rows</td>
                <td className="mono">fire_observations</td>
                <td className="mono">jamie</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>job_4819</td>
              </tr>
              <tr style={{background:'#fff7e6'}}>
                <td></td>
                <td className="mono">13:42:14</td>
                <td><Badge kind="warn">warning</Badge></td>
                <td><span className="tag">sync</span></td>
                <td className="mono">prod</td>
                <td>Sync conflict · replica <span className="mono">field-tablet-3</span> · 14 conflicted rows</td>
                <td className="mono">parcels_2024</td>
                <td className="mono">k.tan</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>sync_812</td>
              </tr>
              <tr>
                <td></td>
                <td className="mono">12:48:20</td>
                <td><Badge>info</Badge></td>
                <td><span className="tag">release</span></td>
                <td className="mono">staging</td>
                <td><b>Release applied</b> · 7 semantic changes · CI green · zero downtime</td>
                <td className="muted">7 items</td>
                <td className="mono">jamie</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>rel_2099</td>
              </tr>
              <tr>
                <td></td>
                <td className="mono">12:14:00</td>
                <td><Badge>info</Badge></td>
                <td><span className="tag">data</span></td>
                <td className="mono">dev</td>
                <td>Schema drift detected · fire_perimeters · 1 column added</td>
                <td className="mono">fire_perimeters</td>
                <td className="mono">system</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>scan_4f</td>
              </tr>
              <tr>
                <td></td>
                <td className="mono">11:02:14</td>
                <td><Badge>info</Badge></td>
                <td><span className="tag">audit</span></td>
                <td className="mono">prod</td>
                <td>Access rule changed · public-works-fs · added origin maps.partner.gov</td>
                <td className="mono">public-works-fs</td>
                <td className="mono">k.tan</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>req_b401</td>
              </tr>
              <tr>
                <td></td>
                <td className="mono">10:14:08</td>
                <td><Badge>info</Badge></td>
                <td><span className="tag">job</span></td>
                <td className="mono">prod</td>
                <td>Auto-scaled · 6 tasks → 8 tasks (CPU)</td>
                <td className="mono">prod fleet</td>
                <td className="mono">system</td>
                <td className="mono" style={{fontSize:9.5,color:'#888'}}>scale_27</td>
              </tr>
            </tbody>
          </table>
        </div>

        <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', background:'#fff', display:'flex', alignItems:'center', gap:8, fontSize:11}}>
          <span className="muted">11 of 1,108 events · last 24h</span>
          <div style={{flex:1}}/>
          <Btn ghost sm>Pin selected to investigation</Btn>
          <Btn ghost sm>← older</Btn>
          <Btn ghost sm>newer →</Btn>
        </div>
      </div>
    </div>
  );
}

function EventDetailDrawer() {
  // Event viewer with the selected critical alert open as a right-side drawer.
  return (
    <div className="scr" style={{position:'relative'}}>
      <TopBar crumbs={['Operate','Event viewer']} env="prod" area="operate" />
      <Sidebar active="activity" />
      <div className="main">
        <PageHead title="Event viewer" sub="Critical event selected · drawer open" />

        {/* faked dimmed list behind drawer */}
        <div style={{padding:'8px 18px', overflow:'auto', flex:1, opacity:0.55}}>
          <table className="tbl tbl--cmpt" style={{fontSize:11}}>
            <thead><tr><th>Time</th><th>Severity</th><th>Type</th><th>Message</th><th>Resource</th></tr></thead>
            <tbody>
              <tr style={{background:'#fbeae7'}}><td className="mono">14:14:22</td><td><Badge kind="bad">critical</Badge></td><td><span className="tag">alert</span></td><td>Task unhealthy · OOMKilled during tile build</td><td className="mono">prod-job-f88</td></tr>
              <tr><td className="mono">14:14:18</td><td><Badge kind="bad">error</Badge></td><td><span className="tag">log</span></td><td className="mono" style={{fontSize:10}}>memory limit exceeded</td><td className="mono">prod-job-f88</td></tr>
              <tr><td className="mono">14:06:10</td><td><Badge kind="warn">warning</Badge></td><td><span className="tag">alert</span></td><td>p95 latency degraded · features-public</td><td className="mono">features-public</td></tr>
              <tr><td className="mono">14:05:42</td><td><Badge>info</Badge></td><td><span className="tag">job</span></td><td>Publish completed · parcels-use-map v4</td><td className="mono">parcels-use-map</td></tr>
            </tbody>
          </table>
        </div>
      </div>

      {/* Drawer */}
      <div className="drawer" style={{width:520}}>
        <h2>
          <Badge kind="bad">critical</Badge>
          <span style={{fontSize:13, fontWeight:600, marginLeft:6}}>Task unhealthy</span>
          <div style={{flex:1}}/>
          <span className="mono muted" style={{fontSize:10}}>evt_91d4</span>
          <span style={{cursor:'pointer'}}>×</span>
        </h2>
        <div style={{padding:'12px 14px', overflow:'auto', flex:1}}>
          <div style={{fontSize:11.5, lineHeight:1.5, marginBottom:12}}>
            <div className="row" style={{marginBottom:4}}>
              <span className="muted" style={{flex:1}}>Time</span>
              <span className="mono">2026-05-23 14:14:22 UTC</span>
            </div>
            <div className="row" style={{marginBottom:4}}>
              <span className="muted" style={{flex:1}}>Env</span>
              <span><span style={{width:7,height:7,borderRadius:'50%',background:'#1d6b3e',display:'inline-block',marginRight:4}}/>prod · us-west-2</span>
            </div>
            <div className="row" style={{marginBottom:4}}>
              <span className="muted" style={{flex:1}}>Source</span>
              <span>fleet.task.unhealthy</span>
            </div>
            <div className="row" style={{marginBottom:4}}>
              <span className="muted" style={{flex:1}}>Rule</span>
              <a className="mono fs-override" style={{fontSize:11}}>memory-oom-killed</a>
            </div>
            <div className="row" style={{marginBottom:4}}>
              <span className="muted" style={{flex:1}}>Owner</span>
              <span>tile-team (auto)</span>
            </div>
          </div>

          <div className="card" style={{padding:'8px 10px', marginBottom:10, background:'#fff7e6', borderLeft:'3px solid var(--warn)'}}>
            <div className="row" style={{marginBottom:4}}>
              <b style={{fontSize:11.5}}>🤖 AI DevOps summary</b>
              <Badge kind="warn" style={{marginLeft:6}}>advisory</Badge>
              <div style={{flex:1}}/>
              <a className="fs-override" style={{fontSize:10}}>view evidence</a>
            </div>
            <div style={{fontSize:11, lineHeight:1.5}}>
              Tile-build worker <span className="mono">prod-job-f88</span> OOMed at z14 for parcels_2024. p95 memory has been climbing 4d → likely related to recent class-breaks v4 (more vertices retained). Suggested actions: bump memory limit to 4GB, or re-tile at lower max-zoom. Two similar OOMs occurred on staging tile-builds last week.
            </div>
          </div>

          {/* Related */}
          <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:4}}>Related objects</div>
          <div className="col" style={{gap:4, marginBottom:10}}>
            {[
              ['◇','parcels_2024','data resource v4'],
              ['◐','Parcels heatmap (FY24)','map · uses this style'],
              ['⇋','Daily refresh · parcels','workflow · scheduled'],
              ['▤','public-works-fs','service · 8 layers'],
              ['📦','prod-job-f88','task · unhealthy'],
              ['📋','release rel_2099','last applied 12h ago'],
            ].map((r,i) => (
              <div key={i} className="row" style={{padding:'4px 8px', border:'1px solid #e4e4e4', borderRadius:4, background:'#fff', fontSize:11}}>
                <span style={{width:14,textAlign:'center',color:'#666'}}>{r[0]}</span>
                <span style={{flex:1, fontWeight:600}}>{r[1]}</span>
                <span className="muted" style={{fontSize:10}}>{r[2]}</span>
                <span style={{color:'var(--pencil)',cursor:'pointer',fontSize:11,marginLeft:6}}>↗</span>
              </div>
            ))}
          </div>

          {/* Evidence */}
          <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:4}}>Raw evidence</div>
          <div className="col" style={{gap:4, marginBottom:10}}>
            {[
              ['Container log · prod-job-f88 · last 5min','log',null],
              ['Memory profile · prod-job-f88','metric',null],
              ['Job run · job_4821 (tile-build)','job',null],
              ['Auto-scale history · prod fleet','metric',null],
              ['Alert rule definition · memory-oom-killed','rule',null],
            ].map((r,i) => (
              <a key={i} className="row" style={{padding:'4px 8px', border:'1px solid #e4e4e4', borderRadius:4, background:'#fff', fontSize:11, color:'var(--pencil)', textDecoration:'underline dotted', cursor:'pointer'}}>
                <span style={{flex:1}}>{r[0]}</span>
                <span className="tag">{r[1]}</span>
              </a>
            ))}
          </div>

          {/* Lifecycle */}
          <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:4}}>Alert lifecycle</div>
          <div className="col" style={{gap:3, fontSize:11, marginBottom:10}}>
            <div className="row"><span style={{flex:1, color:'var(--bad)'}}>● firing</span><span className="muted">14m</span></div>
            <div className="row"><span style={{flex:1, color:'#888'}}>○ acknowledged</span><span className="muted">—</span></div>
            <div className="row"><span style={{flex:1, color:'#888'}}>○ resolved</span><span className="muted">—</span></div>
          </div>
        </div>

        <div style={{padding:'10px 14px', borderTop:'1px solid #e4e4e4', display:'flex', gap:6, background:'#fafafa'}}>
          <Btn ghost sm>Suppress…</Btn>
          <div style={{flex:1}}/>
          <Btn sm>+ Pin to investigation</Btn>
          <Btn sm>Acknowledge</Btn>
          <Btn kind="p" sm>Open runbook ↗</Btn>
        </div>
      </div>
    </div>
  );
}

function Investigation() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Investigations','Tile builds OOMing']} env="prod" area="operate" />
      <Sidebar active="activity" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Investigations <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>Tile builds OOMing on parcels</h1>
            <Badge kind="bad" lg>open</Badge>
            <span className="muted" style={{fontSize:11}}>opened 14m ago · owner jamie · 6 pinned events</span>
            <div style={{flex:1}}/>
            <Btn ghost>Share link</Btn>
            <Btn>Mark resolved</Btn>
            <Btn kind="p">+ Pin event</Btn>
          </div>
        </div>

        <div style={{display:'grid', gridTemplateColumns:'1fr 320px', flex:1, overflow:'hidden'}}>
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            {/* Notes */}
            <div className="card" style={{padding:'10px 14px', marginBottom:12}}>
              <div className="row" style={{marginBottom:6}}>
                <h3 style={{margin:0}}>Investigation notes</h3>
                <div style={{flex:1}}/>
                <Btn ghost sm>+ Note</Btn>
              </div>
              <div className="col" style={{gap:10}}>
                <div>
                  <div className="muted" style={{fontSize:10}}>jamie · 14m ago</div>
                  <div style={{padding:'6px 8px', background:'#fafafa', border:'1px solid #e4e4e4', borderRadius:4, fontSize:11, marginTop:2}}>
                    Started investigating after the 14:14 OOM alert. Two similar OOMs on staging last week — feels related to v4 style change which retains more vertices at z14.
                  </div>
                </div>
                <div>
                  <div className="muted" style={{fontSize:10}}>k.tan · 9m ago</div>
                  <div style={{padding:'6px 8px', background:'#fafafa', border:'1px solid #e4e4e4', borderRadius:4, fontSize:11, marginTop:2}}>
                    Pulled tile-cache memory profile · job-f88 climbed from 1.2GB to 1.84GB over the past 5 hours. The peak coincides with tile pyramid rebuild.
                  </div>
                </div>
                <div>
                  <div className="muted" style={{fontSize:10}}>jamie · 4m ago</div>
                  <div style={{padding:'6px 8px', background:'#fafafa', border:'1px solid #e4e4e4', borderRadius:4, fontSize:11, marginTop:2}}>
                    Provisional fix: bump tile-build worker memory to 4GB in next release. Verifying that doesn't blow the autoscale budget.
                  </div>
                </div>
              </div>
            </div>

            {/* Pinned events */}
            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3 style={{margin:0}}>Pinned events · 6</h3>
                <div className="muted" style={{fontSize:10.5}}>chronological · click any to open in drawer</div>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>When</th><th>Sev</th><th>Type</th><th>Message</th><th>Env</th></tr></thead>
                <tbody>
                  <tr><td className="mono">14:14</td><td><Badge kind="bad">critical</Badge></td><td><span className="tag">alert</span></td><td><b>Task unhealthy</b> · OOMKilled (parcels z14)</td><td className="mono">prod</td></tr>
                  <tr><td className="mono">14:14</td><td><Badge kind="bad">error</Badge></td><td><span className="tag">log</span></td><td><span className="mono" style={{fontSize:10}}>memory limit exceeded</span></td><td className="mono">prod</td></tr>
                  <tr><td className="mono">14:06</td><td><Badge kind="warn">warning</Badge></td><td><span className="tag">alert</span></td><td>p95 latency degraded · features-public</td><td className="mono">prod</td></tr>
                  <tr><td className="mono">14:05</td><td><Badge>info</Badge></td><td><span className="tag">job</span></td><td>Publish completed · parcels-use-map v4</td><td className="mono">dev</td></tr>
                  <tr><td className="mono">3d</td><td><Badge kind="bad">critical</Badge></td><td><span className="tag">alert</span></td><td>Task unhealthy · OOMKilled (parcels z14) · staging</td><td className="mono">staging</td></tr>
                  <tr><td className="mono">5d</td><td><Badge kind="warn">warning</Badge></td><td><span className="tag">alert</span></td><td>Memory usage trend · prod-job-f88 + 32% / week</td><td className="mono">prod</td></tr>
                </tbody>
              </table>
            </div>
          </div>

          {/* Side */}
          <div style={{borderLeft:'1px solid #e4e4e4', padding:'14px 14px', overflow:'auto', background:'#fafafa'}}>
            <div className="card">
              <h3>Investigation</h3>
              <dl className="kv">
                <dt>Opened</dt><dd>14m ago</dd>
                <dt>Owner</dt><dd className="mono">jamie</dd>
                <dt>Status</dt><dd><Badge kind="bad">open</Badge></dd>
                <dt>Severity</dt><dd><Badge kind="bad">critical</Badge></dd>
                <dt>Affected resources</dt><dd>parcels_2024, Parcels heatmap</dd>
                <dt>Env</dt><dd>prod, staging</dd>
              </dl>
            </div>

            <div className="card" style={{background:'#fff7e6', borderLeft:'3px solid var(--warn)'}}>
              <h3>🤖 AI suggested next steps</h3>
              <ol style={{margin:'4px 0 0 16px', padding:0, fontSize:11, lineHeight:1.6}}>
                <li>Raise <span className="mono">tile-build worker</span> memory to 4GB in next release</li>
                <li>Re-tile parcels_2024 at lower max-zoom (12 instead of 14)</li>
                <li>Add memory-trend alert for tile workers (preempt OOM)</li>
              </ol>
              <div className="muted" style={{fontSize:10, marginTop:4}}>advisory · evidence linked</div>
            </div>

            <Ann red>investigations preserve evidence for postmortem. closing flags the linked events as "investigated".</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { EventViewerList, EventDetailDrawer, Investigation });
