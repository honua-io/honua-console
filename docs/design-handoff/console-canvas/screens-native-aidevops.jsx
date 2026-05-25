// Honua Console · Native host + AI DevOps advisory surface
// 4 screens:
//   NativeHostFirstRun     — env profile setup on first launch (mTLS, trust state, endpoint)
//   NativeHostProfiles     — managing multiple env profiles in the native shell
//   AIDevopsConsole        — standalone advisory home aggregating across systems
//   AIDevopsBrief          — single AI-generated brief for one incident pattern

function NativeHostFirstRun() {
  return (
    <div className="scr scr--full" style={{background:'#1f2329', color:'#d8d8d8', display:'flex', flexDirection:'column'}}>
      {/* Native-app chrome */}
      <div style={{height:32, background:'#252a31', borderBottom:'1px solid #0a0c10', display:'flex', alignItems:'center', padding:'0 12px', gap:8, fontSize:11, color:'#9aa3ad'}}>
        <div style={{display:'flex', gap:6}}>
          <span style={{width:10, height:10, borderRadius:'50%', background:'#ff5f57'}}/>
          <span style={{width:10, height:10, borderRadius:'50%', background:'#ffbd2e'}}/>
          <span style={{width:10, height:10, borderRadius:'50%', background:'#28c940'}}/>
        </div>
        <div style={{flex:1, textAlign:'center'}}>Honua Console · native</div>
        <div className="mono" style={{fontSize:10}}>v0.9.0-beta · offline-capable</div>
      </div>

      <div style={{flex:1, display:'grid', placeItems:'center', padding:24}}>
        <div style={{width:640, background:'#252a31', border:'1px solid #3a4554', borderRadius:8, overflow:'hidden'}}>
          <div style={{padding:'18px 20px', borderBottom:'1px solid #3a4554', display:'flex',alignItems:'center',gap:10}}>
            <div style={{
              width:32, height:32, background:'#ffe55c', color:'#141414',
              border:'1.5px solid #141414', borderRadius:6,
              display:'grid', placeItems:'center', fontWeight:700, fontSize:14,
            }}>H</div>
            <div>
              <div style={{font:'600 14px var(--ui)', color:'#fff'}}>Welcome to Honua Console</div>
              <div className="muted" style={{fontSize:10.5, color:'#9aa3ad'}}>Set up your first environment profile to start</div>
            </div>
          </div>

          <div style={{padding:'18px 20px'}}>
            <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:8, color:'#9aa3ad'}}>Profile</div>

            <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10, marginBottom:12}}>
              <div>
                <div style={{fontSize:10.5, color:'#9aa3ad', marginBottom:3}}>Profile name</div>
                <input readOnly className="inp" style={{background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8'}} defaultValue="Public Works · dev" />
              </div>
              <div>
                <div style={{fontSize:10.5, color:'#9aa3ad', marginBottom:3}}>Endpoint URL</div>
                <input readOnly className="inp inp--mono" style={{background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8'}} defaultValue="https://honua-dev.example.gov" />
              </div>
              <div>
                <div style={{fontSize:10.5, color:'#9aa3ad', marginBottom:3}}>Auth method</div>
                <div className="sel"><input readOnly className="inp" style={{background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8'}} defaultValue="OIDC · device flow" /></div>
              </div>
              <div>
                <div style={{fontSize:10.5, color:'#9aa3ad', marginBottom:3}}>Client certificate (mTLS)</div>
                <div style={{display:'flex', gap:6}}>
                  <input readOnly className="inp inp--mono" style={{background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8', flex:1}} defaultValue="~/.honua/jamie-pwd-dev.p12" />
                  <button style={{padding:'4px 10px', background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:10.5, cursor:'pointer'}}>Browse…</button>
                </div>
              </div>
            </div>

            <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:8, color:'#9aa3ad'}}>Server trust</div>

            <div style={{padding:'8px 10px', background:'#1a1d22', border:'1px solid #3a4554', borderRadius:5, fontSize:11, marginBottom:10}}>
              <div className="row" style={{marginBottom:4}}>
                <span style={{flex:1, fontWeight:600, color:'#fff'}}>Server certificate</span>
                <span style={{background:'#ecf7f0', color:'#1d6b3e', padding:'1px 6px', borderRadius:3, fontSize:9.5, fontWeight:600, border:'1px solid #8fcfa6'}}>VALID</span>
              </div>
              <div style={{fontFamily:'var(--mono)', fontSize:10, color:'#9aa3ad', lineHeight:1.5}}>
                <div>Subject:  CN=honua-dev.example.gov, O=State GIS</div>
                <div>Issuer:   CN=State GIS Root CA, O=State GIS</div>
                <div>Valid:    2026-01-04 → 2027-01-04 (224 days left)</div>
                <div>SHA-256:  <span style={{color:'#d8d8d8'}}>7f3a:8201:42d7:91c4:b0e5:…</span></div>
              </div>
              <div className="row" style={{marginTop:6, fontSize:10.5}}>
                <span style={{color:'#9aa3ad'}}>chain trust</span>
                <span style={{flex:1}}/>
                <span style={{color:'#28c940'}}>● rooted at State GIS Root CA (trusted in OS keychain)</span>
              </div>
            </div>

            <div style={{padding:'8px 10px', background:'#1a1d22', border:'1px solid #5a4504', borderRadius:5, fontSize:11, marginBottom:10}}>
              <div className="row" style={{marginBottom:2}}>
                <span style={{fontSize:12}}>🔒</span>
                <span style={{flex:1, fontWeight:600, color:'#ffd84d'}}>Pin server certificate?</span>
              </div>
              <div className="muted" style={{fontSize:10.5, color:'#a8a08a'}}>
                Pinning catches MITM attempts on this endpoint. We'll warn loudly if the server presents a different cert next time.
              </div>
              <div className="row" style={{marginTop:6, fontSize:10.5}}>
                <label className="row" style={{gap:6, color:'#d8d8d8'}}><input type="radio" readOnly defaultChecked/> Pin SHA-256 (recommended)</label>
                <label className="row" style={{gap:6, color:'#d8d8d8'}}><input type="radio" readOnly/> Pin public key</label>
                <label className="row" style={{gap:6, color:'#d8d8d8'}}><input type="radio" readOnly/> Don't pin (trust chain only)</label>
              </div>
            </div>

            <div style={{padding:'8px 10px', background:'#1a1d22', border:'1px solid #3a4554', borderRadius:5, fontSize:10.5, color:'#9aa3ad', marginBottom:10}}>
              <div className="row" style={{marginBottom:2}}>
                <span style={{color:'#d8d8d8', fontWeight:600}}>Connection probe</span>
                <span style={{flex:1}}/>
                <span style={{color:'#28c940'}}>● ready</span>
              </div>
              <div style={{fontFamily:'var(--mono)', lineHeight:1.5, fontSize:10}}>
                <div>✓ TLS 1.3 handshake · 184 ms</div>
                <div>✓ mTLS client cert accepted</div>
                <div>✓ OIDC discovery · {`{`}issuer{`}`} reachable</div>
                <div>✓ API version compatible · 2.4.x ↔ console 0.9</div>
                <div>✓ Workspace visible · 1 (Public Works)</div>
              </div>
            </div>
          </div>

          <div style={{padding:'12px 20px', borderTop:'1px solid #3a4554', background:'#1a1d22', display:'flex', gap:8}}>
            <button style={{padding:'6px 14px', background:'transparent', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:5, fontSize:11.5, cursor:'pointer'}}>Cancel</button>
            <div style={{flex:1}}/>
            <button style={{padding:'6px 14px', background:'transparent', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:5, fontSize:11.5, cursor:'pointer'}}>Test again</button>
            <button style={{padding:'6px 14px', background:'#ffe55c', border:'1px solid #ffe55c', color:'#141414', borderRadius:5, fontSize:11.5, cursor:'pointer', fontWeight:600}}>Save profile · sign in →</button>
          </div>
        </div>
      </div>

      <div style={{padding:'8px 14px', borderTop:'1px solid #3a4554', background:'#1a1d22', fontSize:10, color:'#6e7682', display:'flex', alignItems:'center', gap:14}}>
        <span>Honua Console · native</span>
        <span>·</span>
        <span>offline-capable</span>
        <span>·</span>
        <span>mTLS</span>
        <div style={{flex:1}}/>
        <span>profiles encrypted at rest with system keychain</span>
      </div>
    </div>
  );
}

function NativeHostProfiles() {
  return (
    <div className="scr scr--full" style={{background:'#1f2329', color:'#d8d8d8', display:'flex', flexDirection:'column'}}>
      {/* Native-app chrome */}
      <div style={{height:32, background:'#252a31', borderBottom:'1px solid #0a0c10', display:'flex', alignItems:'center', padding:'0 12px', gap:8, fontSize:11, color:'#9aa3ad'}}>
        <div style={{display:'flex', gap:6}}>
          <span style={{width:10, height:10, borderRadius:'50%', background:'#ff5f57'}}/>
          <span style={{width:10, height:10, borderRadius:'50%', background:'#ffbd2e'}}/>
          <span style={{width:10, height:10, borderRadius:'50%', background:'#28c940'}}/>
        </div>
        <div style={{flex:1, textAlign:'center'}}>Honua Console — Public Works · prod</div>
        <div className="mono" style={{fontSize:10}}>v0.9.0-beta</div>
      </div>

      {/* In-shell profile switcher panel */}
      <div style={{flex:1, display:'flex', overflow:'hidden'}}>
        {/* sidebar profiles */}
        <div style={{width:280, borderRight:'1px solid #3a4554', background:'#252a31', overflow:'auto'}}>
          <div style={{padding:'10px 14px', borderBottom:'1px solid #3a4554', display:'flex', alignItems:'center', gap:8}}>
            <span style={{fontSize:10.5, color:'#9aa3ad', textTransform:'uppercase', letterSpacing:'0.06em', flex:1}}>Profiles</span>
            <span style={{color:'#28c940', fontSize:11, cursor:'pointer'}}>+ Add</span>
          </div>
          {[
            { n:'Public Works · dev', e:'honua-dev.example.gov', st:'active', ws:'Public Works', env:'dev', dot:'#2a6fdb' },
            { n:'Public Works · staging', e:'honua-staging.example.gov', st:'ok', ws:'Public Works', env:'staging', dot:'#d97706' },
            { n:'Public Works · prod', e:'honua.example.gov', st:'on', ws:'Public Works', env:'prod', dot:'#1d6b3e' },
            { n:'BI Internal · dev', e:'bi-dev.example.gov', st:'cert-warn', ws:'BI Internal', env:'dev', dot:'#2a6fdb' },
            { n:'Demo lab', e:'demo.honua.io', st:'expired', ws:'Demo', env:'sandbox', dot:'#888' },
          ].map(p => (
            <div key={p.n} style={{
              padding:'10px 14px', borderBottom:'1px solid #1a1d22',
              background: p.st === 'on' ? '#3a4554' : 'transparent',
              borderLeft: p.st === 'on' ? '3px solid #ffe55c' : '3px solid transparent',
              cursor: 'pointer', opacity: p.st === 'expired' ? 0.55 : 1,
            }}>
              <div className="row" style={{gap:6}}>
                <span style={{width:8, height:8, borderRadius:'50%', background:p.dot}}/>
                <span style={{fontSize:11.5, fontWeight:600, color: p.st === 'on' ? '#fff' : '#d8d8d8'}}>{p.n}</span>
                {p.st === 'cert-warn' && <span style={{background:'#fff7e6', color:'#8a5a04', padding:'1px 5px', borderRadius:3, fontSize:9, fontWeight:600}}>CERT</span>}
                {p.st === 'expired' && <span style={{background:'#fbeae7', color:'#8a2218', padding:'1px 5px', borderRadius:3, fontSize:9, fontWeight:600}}>EXPIRED</span>}
              </div>
              <div className="muted" style={{fontSize:10, marginTop:2, color:'#6e7682', fontFamily:'var(--mono)'}}>{p.e}</div>
            </div>
          ))}
        </div>

        {/* main: selected profile detail */}
        <div style={{flex:1, padding:'18px 24px', overflow:'auto'}}>
          <div className="row" style={{marginBottom:14}}>
            <h1 style={{margin:0, fontSize:18, fontWeight:600, color:'#fff'}}>Public Works · prod</h1>
            <span style={{background:'#1d6b3e', color:'#fff', padding:'2px 8px', borderRadius:3, fontSize:10, fontWeight:600}}>ON · active session</span>
            <div style={{flex:1}}/>
            <button style={{padding:'4px 10px', background:'transparent', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:11, cursor:'pointer'}}>Open in browser ↗</button>
            <button style={{padding:'4px 10px', background:'transparent', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:11, cursor:'pointer'}}>Sign out</button>
            <button style={{padding:'4px 10px', background:'transparent', border:'1px solid #5a2a26', color:'#e07765', borderRadius:4, fontSize:11, cursor:'pointer'}}>Delete profile</button>
          </div>

          <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:14}}>
            <div style={{background:'#252a31', border:'1px solid #3a4554', borderRadius:6, overflow:'hidden'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #3a4554', fontSize:10.5, color:'#9aa3ad', textTransform:'uppercase', letterSpacing:'0.06em'}}>Connection</div>
              <div style={{padding:'10px 12px', fontSize:11.5, color:'#d8d8d8'}}>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Endpoint</span><span className="mono" style={{fontSize:10.5}}>https://honua.example.gov</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>API version</span><span className="mono">2.4.0</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Auth</span><span>OIDC · jamie@example.gov</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Token expires</span><span>in 23h</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>p95 ping</span><span className="mono" style={{color:'#28c940'}}>142 ms</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>WebSocket</span><span style={{color:'#28c940'}}>● connected</span></div>
              </div>
            </div>

            <div style={{background:'#252a31', border:'1px solid #3a4554', borderRadius:6, overflow:'hidden'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #3a4554', fontSize:10.5, color:'#9aa3ad', textTransform:'uppercase', letterSpacing:'0.06em'}}>mTLS</div>
              <div style={{padding:'10px 12px', fontSize:11.5, color:'#d8d8d8'}}>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Client cert</span><span className="mono" style={{fontSize:10}}>jamie-pwd-prod.p12</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Expires</span><span>2027-01-04</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Server cert</span><span style={{color:'#28c940'}}>● pinned (SHA-256)</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Last seen fingerprint</span><span className="mono" style={{fontSize:10}}>7f3a:8201:42d7…</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Pin matches</span><span style={{color:'#28c940'}}>● yes</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Last verified</span><span>just now</span></div>
              </div>
            </div>

            <div style={{background:'#252a31', border:'1px solid #3a4554', borderRadius:6, overflow:'hidden', gridColumn:'1 / -1'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #3a4554', fontSize:10.5, color:'#9aa3ad', textTransform:'uppercase', letterSpacing:'0.06em'}}>Local state</div>
              <div style={{padding:'10px 12px', fontSize:11.5, color:'#d8d8d8'}}>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Offline cache</span><span className="mono">142 MB · 4 maps · 18 dashboards</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Drafts in flight</span><span className="mono">2 (autosaved)</span></div>
                <div className="row"><span style={{flex:1, color:'#9aa3ad'}}>Profile storage</span><span className="mono" style={{fontSize:10}}>system keychain (macOS)</span></div>
                <div className="row" style={{marginTop:4}}>
                  <span style={{flex:1, color:'#9aa3ad'}}>Audit log forwarded to</span>
                  <span>workspace audit · last sync 2m ago</span>
                </div>
              </div>
            </div>
          </div>

          <div style={{
            marginTop:14, padding:'10px 14px',
            background:'#1a1d22', border:'1px solid #5a4504', borderRadius:6,
            fontSize:11, color:'#a8a08a',
          }}>
            <div className="row">
              <span style={{fontSize:14}}>⚠</span>
              <b style={{color:'#ffd84d'}}>BI Internal · dev</b>
              <span style={{flex:1, marginLeft:8}}>
                server cert changed since last connect. Fingerprint <span className="mono" style={{color:'#d8d8d8'}}>b401:7720:…</span> · expected <span className="mono" style={{color:'#d8d8d8'}}>e7fb:9012:…</span>. Connection blocked until you re-verify.
              </span>
              <button style={{padding:'4px 10px', background:'transparent', border:'1px solid #5a4504', color:'#ffd84d', borderRadius:4, fontSize:11, cursor:'pointer'}}>Review change</button>
            </div>
          </div>
        </div>
      </div>

      <div style={{padding:'6px 14px', borderTop:'1px solid #3a4554', background:'#1a1d22', fontSize:10, color:'#6e7682', display:'flex', alignItems:'center', gap:14}}>
        <span>5 profiles · 1 cert warning · 1 expired</span>
        <div style={{flex:1}}/>
        <span>profiles encrypted at rest · system keychain</span>
      </div>
    </div>
  );
}

function AIDevopsConsole() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','AI DevOps']} env="prod" area="operate" />
      <Sidebar active="alerts" />
      <div className="main">
        <PageHead
          title="AI DevOps"
          sub="Cross-system advisory. Patterns the model spots across alerts, events, releases, and resources. Always advisory · always evidence-linked · never auto-acts."
          actions={<>
            <Btn>Configure model</Btn>
            <Btn>Subscribe</Btn>
          </>}
        />

        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'grid', gridTemplateColumns:'repeat(4, 1fr)', gap:8}}>
          {[
            ['Open briefs', '4', 'new this week'],
            ['Patterns spotted', '12', 'last 30d'],
            ['Suggested actions applied', '8', 'by operators'],
            ['Evidence links', '142', 'across briefs'],
          ].map((t,i) => (
            <div key={i} className="card" style={{padding:'8px 10px', gap:2}}>
              <div className="muted" style={{fontSize:9.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>{t[0]}</div>
              <div style={{font:'600 18px var(--ui)'}}>{t[1]}</div>
              <div className="muted" style={{fontSize:10.5}}>{t[2]}</div>
            </div>
          ))}
        </div>

        <Toolbar
          filters={<>
            <FiltChip on x>state: open</FiltChip>
            <FiltChip>severity: any</FiltChip>
            <FiltChip>area: any</FiltChip>
            <FiltChip>env: prod, staging</FiltChip>
            <FiltChip>+ filter</FiltChip>
          </>}
          right={<span className="muted" style={{fontSize:11}}>4 open briefs · auto-refresh 10m</span>}
        />

        <div style={{overflow:'auto', flex:1, padding:'14px 18px', display:'grid', gridTemplateColumns:'1fr', gap:10}}>
          {/* Brief 1 */}
          <div className="card" style={{padding:'12px 14px', borderLeft:'3px solid var(--warn)', background:'#fffdf3'}}>
            <div className="row" style={{marginBottom:6}}>
              <span style={{fontSize:14}}>🤖</span>
              <b style={{fontSize:13}}>Tile-build workers OOMing on parcels_2024</b>
              <Badge kind="warn">advisory</Badge>
              <Badge kind="bad">critical pattern</Badge>
              <div style={{flex:1}}/>
              <span className="muted" style={{fontSize:10.5}}>opened 14m · evidence: 6 events</span>
              <Btn sm>Open brief →</Btn>
            </div>
            <div style={{fontSize:11.5, lineHeight:1.55}}>
              Three OOMs across prod + staging in 5 days for the same workload. Memory trend climbing 32% / week since the v4 class-breaks style change. Suggested: raise tile-build memory to 4GB · re-tile at lower max-zoom.
            </div>
            <div className="row" style={{marginTop:6, fontSize:10.5, gap:4, flexWrap:'wrap'}}>
              <span className="muted">touches:</span>
              <span className="tag">parcels_2024</span>
              <span className="tag">public-works-fs</span>
              <span className="tag">prod fleet</span>
              <span className="tag">tile-build worker</span>
              <span className="tag">rel_2099</span>
            </div>
          </div>

          {/* Brief 2 */}
          <div className="card" style={{padding:'12px 14px', borderLeft:'3px solid var(--accent-deep)'}}>
            <div className="row" style={{marginBottom:6}}>
              <span style={{fontSize:14}}>🤖</span>
              <b style={{fontSize:13}}>Drift accumulating · prod vs staging</b>
              <Badge kind="warn">advisory</Badge>
              <div style={{flex:1}}/>
              <span className="muted" style={{fontSize:10.5}}>opened 2d · evidence: 14 changes</span>
              <Btn sm>Open brief →</Btn>
            </div>
            <div style={{fontSize:11.5, lineHeight:1.55}}>
              prod is now 14 changes behind staging, oldest 9 days. Breaking change in queue (<span className="mono">fire_perimeters · cause_code drop</span>) suggests another defer is likely. Pattern: prod promotions are slipping ~1 week / month. Consider scheduled release cadence.
            </div>
            <div className="row" style={{marginTop:6, fontSize:10.5, gap:4, flexWrap:'wrap'}}>
              <span className="muted">touches:</span>
              <span className="tag">prod env</span>
              <span className="tag">staging env</span>
              <span className="tag">rel_2104</span>
              <span className="tag">fire_perimeters</span>
            </div>
          </div>

          {/* Brief 3 */}
          <div className="card" style={{padding:'12px 14px', borderLeft:'3px solid var(--pencil)'}}>
            <div className="row" style={{marginBottom:6}}>
              <span style={{fontSize:14}}>🤖</span>
              <b style={{fontSize:13}}>Sync conflicts cluster · 4 replicas, 1 root cause</b>
              <Badge>advisory · info</Badge>
              <div style={{flex:1}}/>
              <span className="muted" style={{fontSize:10.5}}>opened 1h · evidence: 4 replicas</span>
              <Btn sm>Open brief →</Btn>
            </div>
            <div style={{fontSize:11.5, lineHeight:1.55}}>
              All 4 disconnected replicas drift on the same field: <span className="mono">assessed_value</span>. Server publishes happened during a window when replicas were known offline. Recommend: schedule canonical publishes outside field-team active hours (06–18 local).
            </div>
            <div className="row" style={{marginTop:6, fontSize:10.5, gap:4, flexWrap:'wrap'}}>
              <span className="muted">touches:</span>
              <span className="tag">parcels_2024</span>
              <span className="tag">4 replicas</span>
              <span className="tag">publish schedule</span>
            </div>
          </div>

          {/* Brief 4 */}
          <div className="card" style={{padding:'12px 14px', borderLeft:'3px solid #888', opacity:0.85}}>
            <div className="row" style={{marginBottom:6}}>
              <span style={{fontSize:14}}>🤖</span>
              <b style={{fontSize:13}}>Slow query · features-public · parcels_2024 by use_code</b>
              <Badge>advisory · info</Badge>
              <div style={{flex:1}}/>
              <span className="muted" style={{fontSize:10.5}}>opened 5h · evidence: 142 traces</span>
              <Btn sm>Open brief →</Btn>
            </div>
            <div style={{fontSize:11.5, lineHeight:1.55}}>
              p95 query latency climbed from 184ms to 742ms over 4 days. Common pattern: <span className="mono">WHERE use_code = 'X'</span> without spatial bbox. Missing index on <span className="mono">use_code</span>. Suggested: <span className="mono">CREATE INDEX</span> · 18 MB · 4 min downtime estimated.
            </div>
            <div className="row" style={{marginTop:6, fontSize:10.5, gap:4, flexWrap:'wrap'}}>
              <span className="muted">touches:</span>
              <span className="tag">parcels_2024</span>
              <span className="tag">features-public</span>
              <span className="tag">prod-postgis</span>
              <span className="tag">SLO p95</span>
            </div>
          </div>
        </div>

        <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', background:'#fafafa', fontSize:11}}>
          <span className="muted"><b>Advisory only.</b> AI DevOps never modifies running systems. Each brief proposes actions; operators choose to apply, snooze, or dismiss. All briefs are auditable.</span>
        </div>
      </div>
    </div>
  );
}

function AIDevopsBrief() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','AI DevOps','Tile-build OOMs']} env="prod" area="operate" />
      <Sidebar active="alerts" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>AI DevOps <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <span style={{fontSize:18}}>🤖</span>
            <h1 style={{margin:0, font:'600 18px var(--ui)'}}>Tile-build workers OOMing on parcels_2024</h1>
            <Badge kind="warn">advisory</Badge>
            <Badge kind="bad">critical pattern</Badge>
            <span className="muted" style={{fontSize:11}}>brief opened 14m ago · model gpt-honua-1 · confidence 0.86</span>
            <div style={{flex:1}}/>
            <Btn ghost>Snooze 24h</Btn>
            <Btn ghost>Dismiss</Btn>
            <Btn kind="p">Pin to investigation →</Btn>
          </div>
        </div>

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1.2px solid var(--accent-deep)', fontSize:11.5, lineHeight:1.55}}>
          <b>Pattern.</b> Three OOMKills across prod + staging in 5 days for the same tile-build job on parcels_2024 at zoom 14. Memory utilization climbing 32% / week since the v4 style change. Two of three OOMs happened on prod-job-f88; one on staging-job-c01. SLO impact: 1 user-visible latency spike, 1 missed cache rebuild, no service downtime so far.
        </div>

        <div style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', flex:1, overflow:'hidden'}}>
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            {/* Evidence */}
            <div className="card" style={{padding:0, marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3 style={{margin:0}}>Evidence · 6 events</h3>
                <div className="muted" style={{fontSize:10.5}}>raw underlying data the brief is built on · click any to inspect</div>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>When</th><th>Sev</th><th>Type</th><th>Subject</th><th>Env</th></tr></thead>
                <tbody>
                  <tr><td className="mono">14m</td><td><Badge kind="bad">critical</Badge></td><td><span className="tag">alert</span></td><td>Task unhealthy · prod-job-f88 OOMKilled (z14)</td><td className="mono">prod</td></tr>
                  <tr><td className="mono">14m</td><td><Badge kind="bad">error</Badge></td><td><span className="tag">log</span></td><td className="mono" style={{fontSize:10}}>memory limit exceeded · 1.84GB / 2GB</td><td className="mono">prod</td></tr>
                  <tr><td className="mono">3d</td><td><Badge kind="bad">critical</Badge></td><td><span className="tag">alert</span></td><td>Task unhealthy · staging-job-c01 OOMKilled (z14)</td><td className="mono">staging</td></tr>
                  <tr><td className="mono">5d</td><td><Badge kind="warn">warn</Badge></td><td><span className="tag">alert</span></td><td>Memory trend · prod-job-f88 +32% / week</td><td className="mono">prod</td></tr>
                  <tr><td className="mono">5d</td><td><Badge>info</Badge></td><td><span className="tag">release</span></td><td>rel_2089 applied · parcels_2024 style v4 (class breaks)</td><td className="mono">prod</td></tr>
                  <tr><td className="mono">7d</td><td><Badge>info</Badge></td><td><span className="tag">data</span></td><td>Tile cache rebuild · parcels_2024 · 4m 12s (baseline)</td><td className="mono">prod</td></tr>
                </tbody>
              </table>
            </div>

            {/* Suggested actions */}
            <div className="card" style={{padding:0, marginBottom:12, background:'#ecf7f0', borderLeft:'3px solid var(--ok)'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #8fcfa6'}}>
                <h3 style={{margin:0}}>Suggested actions · 3</h3>
                <div className="muted" style={{fontSize:10.5}}>operator must apply · AI never modifies running systems</div>
              </div>
              <div style={{padding:'10px 12px', display:'flex', flexDirection:'column', gap:10}}>
                {[
                  { i:1, t:'Raise tile-build worker memory limit to 4GB', d:'Edit fleet spec for prod + staging job-worker roles. Auto-scale budget accommodates +2GB across 8 tasks.', conf:'high', cost:'no downtime', action:'Open release' },
                  { i:2, t:'Re-tile parcels_2024 at max-zoom 12 instead of 14', d:'Lower vertex retention at higher zooms. Trade-off: visual fidelity at z13+. Cache rebuild ~3min.', conf:'medium', cost:'~12min cache rebuild', action:'Start job' },
                  { i:3, t:'Add memory-trend alert for tile workers', d:'Pre-empt OOM by firing at 70% sustained mem. Would have caught both incidents 2 days early.', conf:'high', cost:'no risk', action:'Add alert rule' },
                ].map(a => (
                  <div key={a.i} style={{padding:'8px 10px', background:'#fff', border:'1px solid #c4d8b6', borderRadius:5}}>
                    <div className="row" style={{marginBottom:4}}>
                      <span style={{
                        width:18, height:18, borderRadius:'50%', background:'#1d6b3e', color:'#fff',
                        display:'inline-flex', alignItems:'center', justifyContent:'center', fontSize:10, fontWeight:700,
                      }}>{a.i}</span>
                      <b style={{fontSize:11.5, color:'#1d6b3e'}}>{a.t}</b>
                      <div style={{flex:1}}/>
                      <Badge>confidence: {a.conf}</Badge>
                      <span className="tag">{a.cost}</span>
                    </div>
                    <div className="muted" style={{fontSize:11, marginBottom:6, marginLeft:24, color:'#444'}}>{a.d}</div>
                    <div className="row" style={{gap:4, marginLeft:24}}>
                      <Btn ghost sm>Skip</Btn>
                      <div style={{flex:1}}/>
                      <Btn ghost sm>Why this?</Btn>
                      <Btn kind="p" sm>{a.action}</Btn>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Counterfactual */}
            <div className="card">
              <h3>What if you do nothing</h3>
              <div style={{fontSize:11.5, lineHeight:1.55}}>
                Memory trend extrapolates to OOM every 2 days by next week. p95 latency spikes during rebuild — likely SLO breach on <span className="mono">features-public</span>. Auto-scale would mask the issue for ~10 days before exceeding budget. Recommended: at minimum apply action 3 (alert rule).
              </div>
            </div>
          </div>

          {/* SIDE */}
          <div style={{borderLeft:'1px solid #e4e4e4', padding:'14px 14px', overflow:'auto', background:'#fafafa'}}>
            <div className="card">
              <h3>Touches</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                {[
                  ['◇','parcels_2024','resource v4'],
                  ['▤','public-works-fs','service (tile slot)'],
                  ['📦','prod-job-f88','task · unhealthy'],
                  ['📦','staging-job-c01','task · recovered'],
                  ['📋','rel_2089','release · style change'],
                  ['◷','prod','env · fleet pressure'],
                  ['◷','staging','env · same pattern'],
                ].map((r,i) => (
                  <div key={i} className="row" style={{padding:'3px 6px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:3}}>
                    <span style={{width:14, textAlign:'center', color:'#666'}}>{r[0]}</span>
                    <span style={{flex:1, fontWeight:600}}>{r[1]}</span>
                    <span className="muted" style={{fontSize:10}}>{r[2]}</span>
                  </div>
                ))}
              </div>
            </div>

            <Callout kind="info">
              <b>Why these actions?</b> Each "Why this?" link opens a reasoning trace: what events the model weighted, which heuristics fired, and which actions it deprioritised.
            </Callout>

            <div className="card">
              <h3>Model</h3>
              <dl className="kv">
                <dt>Model</dt><dd className="mono">gpt-honua-1</dd>
                <dt>Confidence</dt><dd>0.86 · high</dd>
                <dt>Last trained</dt><dd>3w ago</dd>
                <dt>Trace ID</dt><dd className="mono" style={{fontSize:10}}>aiops_91d4</dd>
              </dl>
              <Btn ghost sm>View reasoning trace ↗</Btn>
            </div>

            <Ann red>advisory only. operator approval required for every action.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { NativeHostFirstRun, NativeHostProfiles, AIDevopsConsole, AIDevopsBrief });
