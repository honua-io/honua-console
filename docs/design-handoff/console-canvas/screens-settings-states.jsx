// Settings (Access/Auth/CORS/License/About) + remaining resource tabs (Source, Presentation, Advanced)
// + States gallery

function Settings() {
  return (
    <div className="scr">
      <TopBar crumbs={['Settings']} />
      <Sidebar active="settings" />
      <div className="main">
        <PageHead title="Settings" sub="Server-wide configuration. Most changes take effect immediately; some require a service restart." />
        <div style={{display:'grid', gridTemplateColumns:'180px 1fr', flex:1, overflow:'hidden'}}>
          {/* sub-nav */}
          <div style={{borderRight:'1px solid #e4e4e4', padding:'10px 0', background:'#fafafa', fontSize:11.5}}>
            {[
              { k:'Access', g:'Govern' },
              { k:'Auth providers', g:'Govern', on:true },
              { k:'CORS', g:'Govern' },
              { k:'API keys', g:'Govern' },
              { k:'License', g:'Server' },
              { k:'About & version', g:'Server' },
              { k:'Map preview', g:'Server' },
              { k:'Catalog toggles', g:'Server' },
              { k:'Feature flags', g:'Server' },
              { k:'Webhooks', g:'Integrations' },
              { k:'Notifications', g:'Integrations' },
              { k:'Audit log', g:'Integrations' },
            ].reduce((acc, it) => {
              if (!acc.length || acc[acc.length-1].g !== it.g) acc.push({ g:it.g, items:[] });
              acc[acc.length-1].items.push(it);
              return acc;
            }, []).map((g,i) => (
              <div key={i}>
                <div style={{padding:'8px 14px 4px', fontSize:9.5, textTransform:'uppercase', letterSpacing:'0.08em', color:'#888'}}>{g.g}</div>
                {g.items.map(it => (
                  <div key={it.k} style={{
                    padding:'4px 14px', height:24,
                    background: it.on ? 'var(--accent)' : 'transparent',
                    fontWeight: it.on ? 600 : 400,
                    borderLeft: it.on ? '3px solid #141414' : '3px solid transparent',
                    cursor:'pointer',
                  }}>{it.k}</div>
                ))}
              </div>
            ))}
          </div>

          {/* content: Auth providers + access summary collapsed alongside */}
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Auth providers</h2>
            <div className="muted" style={{fontSize:11.5, marginBottom:14}}>How people sign in. You can have multiple providers active at once.</div>

            <div className="col" style={{gap:10}}>
              <div className="card">
                <div className="row">
                  <h3 style={{flex:1}}>OIDC · Microsoft Entra ID</h3>
                  <Badge kind="ok">Active · primary</Badge>
                  <Btn sm>Edit</Btn>
                </div>
                <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
                  <Field label="Issuer"><Inp mono value="https://login.microsoftonline.com/contoso/v2.0" /></Field>
                  <Field label="Client ID"><Inp mono value="b9e7…b4c8" /></Field>
                  <Field label="Client secret"><Inp mono value="•••••••••• (secret-ref: kv://entra/client)" /></Field>
                  <Field label="Redirect URI"><Inp mono value="https://honua.io/auth/callback" /></Field>
                  <Field label="Role mapping" hint="JWT claim → Honua role">
                    <Inp mono value="groups → role" />
                  </Field>
                  <Field label="Default role"><Sel value="org-read" /></Field>
                </div>
              </div>

              <div className="card">
                <div className="row">
                  <h3 style={{flex:1}}>API keys</h3>
                  <Badge kind="ok">Active · 7 keys</Badge>
                  <Btn sm>+ Issue key</Btn>
                </div>
                <table className="tbl tbl--cmpt">
                  <thead><tr><th>Name</th><th>Owner</th><th>Scopes</th><th>Last used</th><th>Expires</th><th></th></tr></thead>
                  <tbody>
                    <tr><td><b>partner-readonly</b></td><td>partners</td><td className="mono">read:public,read:partners</td><td className="muted">2m</td><td className="muted">2026-12</td><td className="muted">⋯</td></tr>
                    <tr><td><b>tile-warmer</b></td><td>system</td><td className="mono">read:internal</td><td className="muted">1h</td><td className="muted">no expiry</td><td className="muted">⋯</td></tr>
                    <tr><td><b>bi-pipeline</b></td><td>analytics</td><td className="mono">read:internal</td><td className="muted">3h</td><td className="muted">2026-08</td><td className="muted">⋯</td></tr>
                  </tbody>
                </table>
              </div>

              <div className="card">
                <div className="row">
                  <h3 style={{flex:1}}>Anonymous access</h3>
                  <Badge kind="accent">Enabled · scoped</Badge>
                  <Btn sm>Edit</Btn>
                </div>
                <div style={{fontSize:11.5}}>
                  Public users can read resources granted to <b>Public read</b>. They cannot list internal resources or hit non-public services. CORS limits are configured under <a className="mono" style={{color:'var(--pencil)'}}>Settings → CORS</a>.
                </div>
              </div>

              <div className="card">
                <div className="row">
                  <h3 style={{flex:1}}>CORS</h3>
                  <Btn sm>+ Origin</Btn>
                </div>
                <table className="tbl tbl--cmpt">
                  <thead><tr><th>Origin</th><th>Methods</th><th>Headers</th><th>Credentials</th><th>Services</th></tr></thead>
                  <tbody>
                    <tr><td className="mono">https://maps.partner.gov</td><td className="mono">GET, HEAD</td><td className="mono">*</td><td>no</td><td>features-public, tiles-public</td></tr>
                    <tr><td className="mono">https://analytics.bi.example.com</td><td className="mono">GET, POST</td><td className="mono">authorization, x-api-key</td><td>yes</td><td>odata-bi</td></tr>
                    <tr><td className="mono">*</td><td className="mono">GET</td><td className="mono">*</td><td>no</td><td>stac-public, dcat-eu</td></tr>
                  </tbody>
                </table>
              </div>

              <div className="card" style={{display:'grid',gridTemplateColumns:'1fr 1fr', gap:14}}>
                <div>
                  <h3>License</h3>
                  <dl className="kv" style={{marginTop:4}}>
                    <dt>Tier</dt><dd>Honua Enterprise</dd>
                    <dt>Issued to</dt><dd>Contoso GIS</dd>
                    <dt>Valid until</dt><dd>2027-02-01 <span className="muted">(in 9 months)</span></dd>
                    <dt>Resources</dt><dd>128 / unlimited</dd>
                    <dt>Tile storage</dt><dd>142 / 500 GB</dd>
                  </dl>
                  <Btn sm>Upload new license…</Btn>
                </div>
                <div>
                  <h3>About</h3>
                  <dl className="kv" style={{marginTop:4}}>
                    <dt>Server</dt><dd className="mono">honua-server 2.3.0</dd>
                    <dt>API</dt><dd className="mono">v2 · admin v2</dd>
                    <dt>Region</dt><dd>us-west-2</dd>
                    <dt>Started</dt><dd>14 May 2026 · 02:14 UTC</dd>
                    <dt>Build</dt><dd className="mono">sha b7f9…22ce</dd>
                  </dl>
                  <Btn sm>Diagnostics ↗</Btn>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResSource() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="define" sub="source" />
        <div className="detail">
          <div className="col">
            <div className="card">
              <h3>Where this resource comes from</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:10}}>
                <Field label="Source kind"><Sel value="Database table (referenced)" /></Field>
                <Field label="Connection"><Sel value="prod-postgis" /></Field>
                <Field label="Schema"><Sel value="public" /></Field>
                <Field label="Table"><Sel value="parcels_2024" /></Field>
                <Field label="Primary ID column"><Sel value="gid" /></Field>
                <Field label="Geometry column"><Sel value="geom · MultiPolygon · 4326" /></Field>
              </div>
            </div>
            <div className="card">
              <h3>Refresh</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:10}}>
                <Field label="Strategy"><Sel value="Re-read on demand (no copy)" /></Field>
                <Field label="Schedule"><Sel value="Nightly · 02:00 UTC" /></Field>
                <Field label="On schema drift"><Sel value="Warn and pause publish" /></Field>
                <Field label="On rows removed"><Sel value="Warn if > 5%" /></Field>
              </div>
              <Callout kind="info">
                Last refresh 2m ago · 1.28 M rows · no schema drift detected.
                Next scheduled run: tonight 02:00 UTC.
              </Callout>
            </div>
            <div className="card">
              <h3>Capabilities (read from source)</h3>
              <div className="row" style={{gap:6,flexWrap:'wrap'}}>
                {['query','filter','geom-index','count','distinct','sample','order-by','time-window'].map(t => (
                  <Badge key={t} kind="ok">{t}</Badge>
                ))}
                {['write-back','transaction-edit','related-records'].map(t => (
                  <Badge key={t}>not avail.</Badge>
                ))}
              </div>
            </div>
          </div>
          <div className="col">
            <Callout>
              <b>Healthy.</b> Source has been queryable for 28 days running.
              p95 query latency on this resource is 184 ms.
            </Callout>
            <div className="card" style={{gap:4}}>
              <h3>Source provenance</h3>
              <dl className="kv">
                <dt>Original system</dt><dd>State Assessor</dd>
                <dt>Loaded by</dt><dd>jamie</dd>
                <dt>First seen</dt><dd>2025-09-12</dd>
                <dt>Ingest mode</dt><dd>referenced (no copy)</dd>
              </dl>
            </div>
            <Ann>this tab never asks user to pick storageBinding / projectionProfile / etc. those terms live in Advanced.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResPresentation() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="define" sub="presentation" />
        <div style={{display:'grid', gridTemplateColumns:'200px 1fr', flex:1, overflow:'hidden'}}>
          {/* secondary sub-nav */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', padding:'8px 0', fontSize:11.5}}>
            {['Styles','Labels','Popups','Relationships','Events','History'].map((t,i) => (
              <div key={t} style={{
                padding:'6px 12px',
                background: i === 0 ? 'var(--accent)' : 'transparent',
                borderLeft: i === 0 ? '3px solid var(--ink)' : '3px solid transparent',
                fontWeight: i === 0 ? 600 : 400, cursor:'pointer',
              }}>
                {t}
              </div>
            ))}
            <div className="muted" style={{padding:'10px 12px', fontSize:10.5}}>
              Presentation lives on the resource. Per-slot overrides available on each service slot.
            </div>
          </div>

          {/* Styles + Maputnik */}
          <div style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
            {/* canonical / variant picker */}
            <div style={{padding:'8px 14px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, background:'#fff'}}>
              <span className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Style</span>
              <div style={{display:'inline-flex', border:'1px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:10.5}}>
                <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Default</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Print</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Dark</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>+ Variant</div>
              </div>
              <span className="tag mono" style={{fontSize:10}}>MapLibre GL JSON</span>
              <span className="muted" style={{fontSize:11}}>· translates on publish to Esri Renderer JSON (FS/MS), SLD (WMS)</span>
              <div style={{flex:1}}/>
              <span className="muted" style={{fontSize:10.5}}>Edited 2h ago · jamie</span>
              <Btn ghost sm>Discard</Btn>
              <Btn sm>Save</Btn>
              <Btn kind="p" sm>Open in Maputnik ↗</Btn>
            </div>

            {/* MAPUTNIK EMBEDDED EDITOR */}
            <div style={{flex:1, display:'grid', gridTemplateColumns:'220px 1fr 260px', overflow:'hidden', borderTop:'1px solid #d0d0d0', background:'#1f2329'}}>
              {/* Left: style layer list */}
              <div style={{borderRight:'1px solid #0a0c10', background:'#252a31', color:'#d8d8d8', overflow:'auto'}}>
                <div style={{padding:'8px 10px', borderBottom:'1px solid #0a0c10', display:'flex', alignItems:'center', gap:8}}>
                  <span style={{fontSize:10, fontWeight:600, textTransform:'uppercase', letterSpacing:'0.08em', color:'#9aa3ad'}}>Style layers</span>
                  <div style={{flex:1}}/>
                  <span style={{color:'#9aa3ad', fontSize:13, cursor:'pointer'}}>＋</span>
                </div>
                {[
                  { i:'▓', n:'background', t:'background', on:true },
                  { i:'▒', n:'land', t:'fill', on:true },
                  { i:'╱', n:'water', t:'fill', on:true },
                  { i:'─', n:'roads/casing', t:'line', on:true },
                  { i:'─', n:'roads/fill', t:'line', on:true },
                  { i:'▤', n:'parcels/fill', t:'fill', on:true, sel:true },
                  { i:'┌', n:'parcels/outline', t:'line', on:true },
                  { i:'A', n:'parcels/labels', t:'symbol', on:true },
                  { i:'•', n:'hydrants', t:'circle', on:true },
                ].map((l,i) => (
                  <div key={i} style={{
                    padding:'5px 10px', display:'flex', alignItems:'center', gap:6,
                    fontSize:11, borderBottom:'1px solid #1a1d22',
                    background: l.sel ? '#3a4554' : 'transparent',
                    borderLeft: l.sel ? '2px solid var(--accent)' : '2px solid transparent',
                    cursor:'pointer',
                  }}>
                    <span style={{width:14, textAlign:'center', color: l.sel ? 'var(--accent)' : '#9aa3ad', fontFamily:'var(--mono)'}}>{l.i}</span>
                    <span style={{flex:1, color: l.sel ? '#fff' : '#d8d8d8'}}>{l.n}</span>
                    <span style={{fontSize:9, color:'#6e7682'}}>{l.t}</span>
                    <span style={{color:'#9aa3ad', fontSize:11, opacity: l.on ? 1 : 0.3}}>◉</span>
                  </div>
                ))}
                <div style={{padding:'8px 10px', fontSize:10, color:'#6e7682', fontFamily:'var(--mono)'}}>
                  9 layers · style sources from <span style={{color:'#9aa3ad'}}>parcels_2024</span>
                </div>
              </div>

              {/* Middle: map preview */}
              <div style={{position:'relative', background:'#0e1014'}}>
                <div style={{padding:'6px 10px', borderBottom:'1px solid #0a0c10', display:'flex', alignItems:'center', gap:8, color:'#9aa3ad', fontSize:10.5}}>
                  <span>Preview</span>
                  <span style={{color:'#3e4651'}}>·</span>
                  <span className="mono">zoom 14</span>
                  <span style={{color:'#3e4651'}}>·</span>
                  <span className="mono">1:8,000</span>
                  <div style={{flex:1}}/>
                  <span>basemap</span>
                  <span className="mono">positron</span>
                </div>
                <div style={{padding:10}}>
                  <div style={{filter:'invert(1) hue-rotate(180deg)'}}>
                    <MapPreview mode="layer" height={300} popup={false} scaleText="1:8,000" />
                  </div>
                </div>
                <div style={{padding:'6px 10px', borderTop:'1px solid #0a0c10', color:'#6e7682', fontSize:10}}>
                  Live re-render on every edit. <span style={{color:'#9aa3ad'}}>1k sampled features.</span>
                </div>
              </div>

              {/* Right: properties panel for selected layer */}
              <div style={{borderLeft:'1px solid #0a0c10', background:'#252a31', color:'#d8d8d8', overflow:'auto'}}>
                <div style={{padding:'8px 10px', borderBottom:'1px solid #0a0c10'}}>
                  <div className="row">
                    <span style={{fontSize:10, fontWeight:600, textTransform:'uppercase', letterSpacing:'0.08em', color:'#9aa3ad', flex:1}}>parcels/fill</span>
                    <span style={{fontSize:9, color:'#6e7682', fontFamily:'var(--mono)'}}>type: fill</span>
                  </div>
                </div>

                {/* Tabs inside Maputnik panel */}
                <div style={{display:'flex', fontSize:10.5, borderBottom:'1px solid #0a0c10'}}>
                  {['General','Filter','Paint','Layout','JSON'].map((t,i) => (
                    <div key={t} style={{
                      padding:'6px 10px',
                      color: i === 2 ? '#fff' : '#9aa3ad',
                      borderBottom: i === 2 ? '2px solid var(--accent)' : '2px solid transparent',
                      cursor:'pointer'
                    }}>{t}</div>
                  ))}
                </div>

                <div style={{padding:10, fontSize:11}}>
                  {/* fill-color (data-driven) */}
                  <div style={{marginBottom:10}}>
                    <div className="row" style={{marginBottom:4}}>
                      <span style={{color:'#9aa3ad', flex:1}}>fill-color</span>
                      <span className="tag" style={{background:'#3a4554', color:'#d8d8d8', border:'1px solid #4a5564', fontSize:9}}>fn(area_m2)</span>
                    </div>
                    <div style={{border:'1px solid #3a4554', borderRadius:4, padding:6, background:'#1a1d22'}}>
                      {[
                        ['#f7f4e8','0 – 740'],
                        ['#ead78a','740 – 1,420'],
                        ['#d9a23a','1,420 – 2,140'],
                        ['#b56b1c','2,140 – 3,920'],
                        ['#612d0a','3,920+'],
                      ].map(([c,l]) => (
                        <div key={l} className="row" style={{padding:'2px 0', gap:6, fontSize:10}}>
                          <span style={{width:14, height:14, background:c, border:'1px solid #4a5564', display:'inline-block'}} />
                          <span style={{color:'#9aa3ad'}}>{l}</span>
                          <span style={{flex:1}}/>
                          <span className="mono" style={{color:'#6e7682', fontSize:9}}>{c}</span>
                        </div>
                      ))}
                      <div style={{padding:'4px 0 0', fontSize:9, color:'#6e7682', fontFamily:'var(--mono)'}}>
                        + 1 stop · interpolate (linear) on <span style={{color:'#9aa3ad'}}>area_m2</span>
                      </div>
                    </div>
                  </div>

                  {/* fill-opacity */}
                  <div style={{marginBottom:10}}>
                    <div className="row" style={{marginBottom:4}}>
                      <span style={{color:'#9aa3ad', flex:1}}>fill-opacity</span>
                    </div>
                    <div className="row" style={{gap:6}}>
                      <input type="range" defaultValue={88} style={{flex:1, accentColor:'#d9a23a'}} />
                      <span className="mono" style={{fontSize:10, color:'#9aa3ad', width:30, textAlign:'right'}}>0.88</span>
                    </div>
                  </div>

                  {/* fill-antialias */}
                  <div className="row" style={{marginBottom:10}}>
                    <span style={{color:'#9aa3ad', flex:1, fontSize:11}}>fill-antialias</span>
                    <div style={{width:32, height:18, borderRadius:9, background:'#d9a23a', position:'relative'}}>
                      <span style={{position:'absolute',right:2,top:2,width:14,height:14,borderRadius:'50%',background:'#fff'}} />
                    </div>
                  </div>

                  {/* zoom range */}
                  <div style={{marginBottom:10}}>
                    <div className="row" style={{marginBottom:4}}>
                      <span style={{color:'#9aa3ad', flex:1}}>zoom range</span>
                    </div>
                    <div className="row" style={{gap:6, fontSize:10}}>
                      <span className="mono" style={{color:'#6e7682'}}>0</span>
                      <div style={{flex:1, height:4, background:'#1a1d22', borderRadius:2, position:'relative'}}>
                        <div style={{position:'absolute', left:'40%', right:'5%', top:0, bottom:0, background:'var(--accent)', borderRadius:2}} />
                      </div>
                      <span className="mono" style={{color:'#6e7682'}}>24</span>
                    </div>
                    <div className="row" style={{marginTop:3, fontSize:9, color:'#6e7682'}}><span style={{flex:1}}/>min 10 · max 22</div>
                  </div>

                  {/* JSON jump */}
                  <div style={{borderTop:'1px solid #1a1d22', paddingTop:8, marginTop:8}}>
                    <div className="row" style={{fontSize:10, color:'#6e7682'}}>
                      <span>data-driven, expressions, hover</span>
                      <span style={{flex:1}}/>
                      <span style={{color:'var(--accent)', cursor:'pointer'}}>edit JSON →</span>
                    </div>
                  </div>
                </div>

                <div style={{padding:'8px 10px', borderTop:'1px solid #0a0c10', display:'flex', gap:6}}>
                  <button style={{flex:1, padding:'4px 8px', background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:10, cursor:'pointer'}}>Duplicate</button>
                  <button style={{flex:1, padding:'4px 8px', background:'#1a1d22', border:'1px solid #5a2a26', color:'#e07765', borderRadius:4, fontSize:10, cursor:'pointer'}}>Delete</button>
                </div>
              </div>
            </div>

            {/* Footer strip */}
            <div style={{padding:'8px 14px', borderTop:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, background:'#fff', fontSize:11}}>
              <Callout kind="info" style={{margin:0, padding:'4px 8px', flex:1}}>
                <b>Canonical style.</b> Inherits to all service slots. Each slot can override via "Override style" on the slot detail (e.g. Print-style for tile cache, mobile-style for FS/0).
              </Callout>
              <Btn ghost sm>Import .json</Btn>
              <Btn ghost sm>Export .json</Btn>
              <Btn ghost sm>Open in Maputnik ↗</Btn>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResPresentationPopups() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="define" sub="presentation" />
        <div style={{display:'grid', gridTemplateColumns:'200px 1fr', flex:1, overflow:'hidden'}}>
          {/* sub-nav with Popups selected */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', padding:'8px 0', fontSize:11.5}}>
            {['Styles','Labels','Popups','Relationships','Events','History'].map((t,i) => (
              <div key={t} style={{
                padding:'6px 12px',
                background: i === 2 ? 'var(--accent)' : 'transparent',
                borderLeft: i === 2 ? '3px solid var(--ink)' : '3px solid transparent',
                fontWeight: i === 2 ? 600 : 400, cursor:'pointer',
              }}>{t}</div>
            ))}
          </div>

          <div style={{display:'grid', gridTemplateColumns:'1.3fr 1fr', overflow:'hidden'}}>
            {/* LEFT: template editor */}
            <div style={{overflow:'auto', padding:14, borderRight:'1px solid #e4e4e4'}}>
              <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Popup template</h2>
              <div className="muted" style={{fontSize:11.5, marginBottom:12}}>
                Shown on map click in FeatureServer GetFeatureInfo, MapServer Identify, OGC API Features popup. Field tokens are substituted at runtime. Sensitive fields are auto-redacted per access rules.
              </div>

              <div style={{display:'inline-flex', border:'1px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:10.5, marginBottom:10}}>
                <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Visual</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>HTML</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>JSON</div>
              </div>

              {/* visual blocks */}
              <div className="card" style={{padding:0, marginBottom:10}}>
                <div style={{padding:'6px 10px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:6, background:'#fafafa'}}>
                  <span className="muted" style={{fontSize:10, textTransform:'uppercase', letterSpacing:'0.06em'}}>Title</span>
                  <div style={{flex:1}}/>
                  <span className="muted" style={{fontSize:10}}>field tokens: <span className="mono">{'{{ field_name }}'}</span></span>
                </div>
                <div style={{padding:'8px 10px', font:'500 13px var(--ui)'}}>
                  Parcel <span style={{background:'#fffae0', border:'1px dashed var(--ink)', borderRadius:3, padding:'0 4px', fontFamily:'var(--mono)', fontSize:11}}>{'{{ parcel_id }}'}</span>
                  {' · '}
                  <span style={{background:'#fffae0', border:'1px dashed var(--ink)', borderRadius:3, padding:'0 4px', fontFamily:'var(--mono)', fontSize:11}}>{'{{ area_m2 | number }}'}</span>
                  {' m²'}
                </div>
              </div>

              <div className="card" style={{padding:0, marginBottom:10}}>
                <div style={{padding:'6px 10px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:6, background:'#fafafa'}}>
                  <span className="muted" style={{fontSize:10, textTransform:'uppercase', letterSpacing:'0.06em'}}>Body · field list</span>
                  <div style={{flex:1}}/>
                  <Btn ghost sm>+ Add field</Btn>
                </div>
                <table className="tbl tbl--cmpt">
                  <thead><tr>
                    <th style={{width:24}}></th>
                    <th>Field</th><th>Label</th><th>Format</th><th>Visible?</th><th>Sensitive</th>
                  </tr></thead>
                  <tbody>
                    <tr><td className="muted">⋮⋮</td><td className="mono">use_code</td><td>Use</td><td>domain lookup</td><td>✓</td><td className="muted">—</td></tr>
                    <tr><td className="muted">⋮⋮</td><td className="mono">assessed_value</td><td>Assessed</td><td>USD currency</td><td>✓</td><td className="muted">—</td></tr>
                    <tr><td className="muted">⋮⋮</td><td className="mono">last_assessment</td><td>Last assessed</td><td>iso-date</td><td>✓</td><td className="muted">—</td></tr>
                    <tr><td className="muted">⋮⋮</td><td className="mono">owner_name</td><td>Owner</td><td>raw</td><td className="muted">conditional</td><td><Badge kind="warn">PII</Badge></td></tr>
                  </tbody>
                </table>
              </div>

              <div className="card" style={{padding:0, marginBottom:10}}>
                <div style={{padding:'6px 10px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', display:'flex', alignItems:'center', gap:6}}>
                  <span className="muted" style={{fontSize:10, textTransform:'uppercase', letterSpacing:'0.06em'}}>Footer · custom HTML</span>
                  <div style={{flex:1}}/>
                  <Btn ghost sm>Insert token…</Btn>
                </div>
                <pre className="mono" style={{margin:0,padding:'8px 10px',background:'#0e0e0e',color:'#d8d8d8',fontSize:10.5, whiteSpace:'pre-wrap'}}>
{`<a href="https://assessor.ca.gov/parcel/{{ parcel_id }}"
   target="_blank">View on assessor portal ↗</a>
<div class="muted">Updated {{ last_assessment | date }}</div>`}
                </pre>
              </div>

              <div className="row" style={{gap:6, flexWrap:'wrap'}}>
                <Btn sm>+ Image block</Btn>
                <Btn sm>+ Chart block</Btn>
                <Btn sm>+ Related records</Btn>
                <Btn sm>+ Attachments</Btn>
              </div>
            </div>

            {/* RIGHT: live popup preview */}
            <div style={{padding:14, overflow:'auto', background:'#fafafa'}}>
              <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>Preview as</div>
              <div style={{display:'inline-flex', border:'1px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:10.5, marginBottom:12}}>
                <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Public</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Org read</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>GIS editor</div>
              </div>

              {/* popup card preview */}
              <div style={{background:'#fff', border:'1.2px solid #141414', borderRadius:6, boxShadow:'0 4px 16px rgba(0,0,0,.12)', overflow:'hidden', maxWidth:340}}>
                <div style={{padding:'8px 12px', background:'#fffae0', borderBottom:'1px solid #e4e4e4', fontWeight:600, fontSize:13}}>
                  Parcel 04-021-204 · 2,008 m²
                </div>
                <div style={{padding:'8px 12px', fontSize:11.5}}>
                  <div className="row" style={{padding:'3px 0', borderBottom:'1px dashed #eee'}}><span className="muted" style={{flex:1}}>Use</span><span>Single-family residential</span></div>
                  <div className="row" style={{padding:'3px 0', borderBottom:'1px dashed #eee'}}><span className="muted" style={{flex:1}}>Assessed</span><span className="mono">$582,000</span></div>
                  <div className="row" style={{padding:'3px 0', borderBottom:'1px dashed #eee'}}><span className="muted" style={{flex:1}}>Last assessed</span><span>2024-08-12</span></div>
                  <div className="row" style={{padding:'3px 0'}}><span className="muted" style={{flex:1}}>Owner</span><span className="muted">— redacted —</span></div>
                </div>
                <div style={{padding:'6px 12px 8px', borderTop:'1px dashed #e4e4e4', fontSize:11}}>
                  <a style={{color:'var(--pencil)', textDecoration:'underline dotted'}}>View on assessor portal ↗</a>
                  <div className="muted" style={{fontSize:10, marginTop:2}}>Updated 12 Aug 2024</div>
                </div>
              </div>

              <Callout kind="warn" style={{marginTop:12}}>
                <b>Public audience</b> sees a redacted Owner. Switch to GIS editor above to preview unredacted.
              </Callout>

              <div className="card" style={{marginTop:12}}>
                <h3>Applies to</h3>
                <div className="col" style={{gap:4, fontSize:11}}>
                  {[
                    ['FeatureServer · GetFeatureInfo', true],
                    ['MapServer · Identify', true],
                    ['OGC API Features popup', true],
                    ['WMS GetFeatureInfo (HTML)', true],
                    ['WMTS', false],
                    ['STAC / DCAT / OGC Records', false],
                  ].map(([k,on]) => (
                    <div key={k} className="row" style={{padding:'2px 0', borderBottom:'1px dashed #eee'}}>
                      <span style={{flex:1}}>{k}</span>
                      {on ? <Badge kind="ok">applies</Badge> : <span className="muted">n/a</span>}
                    </div>
                  ))}
                </div>
                <Btn ghost sm>Override per slot…</Btn>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResAdvanced() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="define" sub="advanced" />
        <div className="detail">
          <div className="col">
            <Callout kind="warn">
              <b>You're in Advanced.</b> These controls let you override defaults Honua chose for you. Most operators never need to come here. Changes are versioned and auditable.
            </Callout>

            <div className="card">
              <h3>Storage binding</h3>
              <div className="muted" style={{fontSize:11}}>Override which physical storage backs this resource per service target.</div>
              <table className="tbl tbl--cmpt" style={{marginTop:4}}>
                <thead><tr><th>Target</th><th>Storage</th><th>Index</th><th>Override</th></tr></thead>
                <tbody>
                  <tr><td>OGC API Features</td><td className="mono">prod-postgis / parcels_2024</td><td className="mono">spatial_idx</td><td><Badge>inherit</Badge></td></tr>
                  <tr><td>WMTS</td><td className="mono">tile-cache / parcels_2024</td><td className="mono">pyramid 0–14</td><td><Badge kind="accent">overridden</Badge></td></tr>
                  <tr><td>STAC</td><td className="mono">postgres-meta</td><td className="mono">—</td><td><Badge>inherit</Badge></td></tr>
                </tbody>
              </table>
            </div>

            <div className="card">
              <h3>Projection profile (per target)</h3>
              <div className="muted" style={{fontSize:11}}>Free-form per-target output rules. Use only if the published projection differs from the canonical resource.</div>
              <pre className="mono" style={{margin:0,padding:10,background:'#fafafa',border:'1px solid #eee',borderRadius:4,fontSize:10.5,whiteSpace:'pre-wrap'}}>
{`{
  "OGC API Features": { "fields": { "exclude": ["owner_name"] } },
  "STAC":             { "extensions": ["raster","label"] },
  "Esri catalog":     { "thumbnail": "ref://thumb/parcels_v4.png" }
}`}
              </pre>
            </div>

            <div className="card">
              <h3>Raw object inspector</h3>
              <Tabs sub items={[{k:'r',t:'Resource'},{k:'b',t:'Source binding'},{k:'p',t:'Projection profile'},{k:'r2',t:'Runtime snapshot'}]} active="r" />
              <pre className="mono" style={{margin:0,padding:10,background:'#0e0e0e',color:'#d8d8d8',borderRadius:4,fontSize:10.5,maxHeight:160,overflow:'auto',whiteSpace:'pre-wrap'}}>
{`{
  "id":           "honua:parcels:2024",
  "kind":         "feature_dataset",
  "version":      4,
  "fields":       [ /* 24 fields */ ],
  "metadata":     { /* ISO 19115 mapped */ },
  "publications": 4
}`}
              </pre>
            </div>
          </div>
          <div className="col">
            <Callout kind="bad">Editing here can break canonical guarantees. Only available to admins.</Callout>
            <Ann red>internal terms live here intentionally. nowhere else.</Ann>
            <div className="card">
              <h3>Audit · last 5 advanced edits</h3>
              <div className="col" style={{gap:4,fontSize:11}}>
                <div className="row"><span style={{flex:1}}>WMTS storage overridden</span><span className="muted">jamie · 2w</span></div>
                <div className="row"><span style={{flex:1}}>STAC extension added: raster</span><span className="muted">jamie · 3w</span></div>
                <div className="row"><span style={{flex:1}}>Field exclude: owner_name (OGC)</span><span className="muted">k.tan · 5w</span></div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- States gallery ---------- */
function StatesGallery() {
  function Stub({ title, sub, children }) {
    return (
      <div className="card" style={{padding:0, overflow:'hidden', minHeight:240}}>
        <div style={{padding:'6px 10px', background:'#fafafa', borderBottom:'1px solid #e4e4e4', fontSize:10.5}}>
          <b>{title}</b> <span className="muted" style={{marginLeft:6}}>{sub}</span>
        </div>
        <div style={{padding:14, flex:1, display:'flex', flexDirection:'column', justifyContent:'center', alignItems:'center', gap:8, textAlign:'center'}}>
          {children}
        </div>
      </div>
    );
  }
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['States gallery']} />
      <div style={{padding:'14px 18px', overflow:'auto', height:'100%'}}>
        <PageHead title="States" sub="Every list / detail / wizard surface needs all of these." />
        <div style={{display:'grid', gridTemplateColumns:'repeat(3, 1fr)', gap:14, padding:'12px 18px'}}>
          <Stub title="Empty" sub="first run">
            <div style={{width:36,height:36,border:'1.5px dashed #aaa',borderRadius:'50%',display:'grid',placeItems:'center',fontSize:18,color:'#999'}}>◇</div>
            <div style={{font:'600 13px var(--ui)'}}>No data resources yet</div>
            <div className="muted" style={{fontSize:11,maxWidth:240}}>Connect to a database or upload a file to create the first one.</div>
            <div className="row" style={{marginTop:6}}>
              <Btn>+ Connection</Btn>
              <Btn kind="p">+ From table</Btn>
            </div>
            <Ann>show 1–3 most likely actions, never just art.</Ann>
          </Stub>

          <Stub title="Loading" sub="skeleton">
            <div style={{width:'100%'}}>
              {[1,1,0.85,0.7,0.95,0.55,0.8].map((w,i) => (
                <div key={i} style={{height:14, marginBottom:6, background:`linear-gradient(90deg,#eee,#f8f8f8,#eee)`, width:(w*100)+'%', borderRadius:3}} />
              ))}
            </div>
          </Stub>

          <Stub title="Warning" sub="non-blocking">
            <div style={{width:42,height:42,border:'1.5px solid var(--warn)',borderRadius:'50%',display:'grid',placeItems:'center',fontSize:22,color:'var(--warn)',background:'#fff7e6'}}>!</div>
            <div style={{font:'600 13px var(--ui)'}}>Schema drift detected</div>
            <div className="muted" style={{fontSize:11,maxWidth:240}}>The source table added one column since last refresh. Publish still works.</div>
            <div className="row" style={{marginTop:6}}>
              <Btn>Ignore</Btn>
              <Btn kind="p">Review change</Btn>
            </div>
          </Stub>

          <Stub title="Blocked" sub="must fix">
            <div style={{width:42,height:42,border:'1.5px solid var(--bad)',borderRadius:'50%',display:'grid',placeItems:'center',fontSize:22,color:'var(--bad)',background:'#fbeae7'}}>✕</div>
            <div style={{font:'600 13px var(--ui)'}}>Cannot publish to OGC Features</div>
            <div className="muted" style={{fontSize:11,maxWidth:240}}>CRS must be set on every geometry. 2 rows are missing it.</div>
            <div className="row" style={{marginTop:6}}>
              <Btn>See rows</Btn>
              <Btn kind="p">Auto-fix CRS</Btn>
            </div>
          </Stub>

          <Stub title="Partial success" sub="don't hide wins">
            <div style={{width:42,height:42,border:'1.5px solid var(--warn)',borderRadius:'50%',display:'grid',placeItems:'center',fontSize:22,color:'var(--warn)',background:'#fff7e6'}}>~</div>
            <div style={{font:'600 13px var(--ui)'}}>2 of 3 layers imported</div>
            <div className="muted" style={{fontSize:11,maxWidth:260}}>parcels &amp; tax_events imported. parcel_centroids failed at row 1.04 M (geometry invalid).</div>
            <div className="row" style={{marginTop:6}}>
              <Btn>Open job</Btn>
              <Btn kind="p">Retry failed only</Btn>
            </div>
          </Stub>

          <Stub title="Success" sub="actionable next step">
            <div style={{width:42,height:42,border:'1.5px solid var(--ok)',borderRadius:'50%',display:'grid',placeItems:'center',fontSize:22,color:'var(--ok)',background:'#ecf7f0'}}>✓</div>
            <div style={{font:'600 13px var(--ui)'}}>Published to 4 targets</div>
            <div className="muted" style={{fontSize:11,maxWidth:240}}>parcels_2024 v4 is live on OGC Features, STAC, WMTS, and DCAT.</div>
            <div className="row" style={{marginTop:6}}>
              <Btn>Copy share links</Btn>
              <Btn kind="p">Open resource</Btn>
            </div>
          </Stub>

          <Stub title="Error · system" sub="rare">
            <div style={{width:42,height:42,border:'1.5px solid var(--bad)',borderRadius:'50%',display:'grid',placeItems:'center',fontSize:22,color:'var(--bad)',background:'#fbeae7'}}>!</div>
            <div style={{font:'600 13px var(--ui)'}}>Something went wrong</div>
            <div className="muted" style={{fontSize:11,maxWidth:240}}>We saved your draft. Trace ID <span className="mono">req-7f3a…</span>.</div>
            <div className="row" style={{marginTop:6}}><Btn>Retry</Btn><Btn ghost>Report</Btn></div>
          </Stub>

          <Stub title="Permission denied" sub="read-only role">
            <div style={{width:42,height:42,border:'1.5px solid #888',borderRadius:'50%',display:'grid',placeItems:'center',fontSize:22,color:'#666'}}>⚿</div>
            <div style={{font:'600 13px var(--ui)'}}>You don't have permission to publish</div>
            <div className="muted" style={{fontSize:11,maxWidth:240}}>Ask a publisher to take this over, or request the publisher-edit role.</div>
            <div className="row" style={{marginTop:6}}><Btn>Request role</Btn><Btn ghost>Copy link</Btn></div>
          </Stub>

          <Stub title="Filtered-empty" sub="filters too strict">
            <div style={{font:'600 13px var(--ui)'}}>No resources match</div>
            <div className="muted" style={{fontSize:11,maxWidth:240}}>Try removing a filter. You have 128 resources total.</div>
            <div className="row" style={{marginTop:6,gap:4,flexWrap:'wrap'}}>
              <FiltChip x>source: prod-postgis</FiltChip>
              <FiltChip x>type: raster</FiltChip>
              <FiltChip x>blocked</FiltChip>
            </div>
            <Btn ghost sm>Clear all</Btn>
          </Stub>
        </div>
      </div>
    </div>
  );
}

function ResSourceImported() {
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_v3']} />
      <Sidebar active="resources" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Data resources <span style={{color:'#bbb'}}>/</span> imported <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <h1 style={{margin:0,font:'600 18px var(--ui)'}}>
              <span style={{color:'var(--pencil)'}}>◇</span> parcels_v3
            </h1>
            <Badge kind="ok" lg>Published</Badge>
            <span className="muted" style={{fontSize:11}}>migrated once from remote service · 3 weeks ago</span>
            <div style={{flex:1}}/>
            <Btn ghost>Preview</Btn>
            <Btn>Migrate again…</Btn>
            <Btn kind="p">Publish…</Btn>
          </div>
          <div className="muted" style={{fontSize:11.5, marginTop:6}}>
            Statewide parcels v3, migrated as a one-time copy off of state-gis FeatureServer. Now lives in Honua's managed storage — the original service is no longer in the loop.
          </div>
        </div>
        <SuperTabs on="define" sub="source" />
        <div className="detail">
          <div className="col">
            <Callout kind="info">
              <b>Migration import.</b> Honua copied this data once from the remote service. It now lives in Honua-managed storage; we don't proxy, sync, or poll the remote. To pick up later changes from the source, run <b>Migrate again</b> — you choose whether to overwrite or version-up. Honua holds no credentials to that service.
            </Callout>

            <div className="card">
              <h3>Provenance — where this came from</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:10}}>
                <Field label="Source kind"><Sel value="Remote service · GeoServices FeatureServer" /></Field>
                <Field label="Migrated on"><Inp mono value="2026-03-14 09:42 UTC" /></Field>
                <Field label="Source URL">
                  <Inp mono value="https://services.example.com/.../Parcels/FeatureServer/0" />
                </Field>
                <Field label="Source layer ID"><Inp mono value="0" /></Field>
                <Field label="Migrated by"><Inp value="k.tan via job #2298" /></Field>
                <Field label="Source CRS"><Inp mono value="EPSG:4326 (preserved)" /></Field>
              </div>
              <Callout kind="warn">
                <b>No back-link to the remote service.</b> Honua doesn't store credentials and won't reach back unless an operator explicitly runs Migrate again.
              </Callout>
            </div>

            <div className="card">
              <h3>Where this resource actually lives now</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:10}}>
                <Field label="Storage"><Sel value="prod-postgis · honua_imports.parcels_v3" /></Field>
                <Field label="Primary ID column"><Sel value="gid" /></Field>
                <Field label="Geometry column"><Sel value="geom · MultiPolygon · 4326" /></Field>
                <Field label="Row count"><Inp mono value="1,284,021" /></Field>
              </div>
              <div className="muted" style={{fontSize:11}}>
                After import, this resource behaves like any other database-backed resource. You can edit its fields, metadata, presentation, and publish it normally.
              </div>
            </div>

            <div className="card">
              <h3>Migration history</h3>
              <table className="tbl tbl--cmpt">
                <thead><tr><th>Version</th><th>When</th><th>By</th><th>Job</th><th>Layers</th><th>Result</th></tr></thead>
                <tbody>
                  <tr><td className="mono"><b>v3</b></td><td>2026-03-14</td><td>k.tan</td><td className="mono">#2298</td><td>1 of 1</td><td><Badge kind="ok">3 resources created</Badge></td></tr>
                  <tr><td className="mono">v2</td><td>2025-11-04</td><td>jamie</td><td className="mono">#1740</td><td>1 of 1</td><td><Badge kind="ok">overwrite</Badge></td></tr>
                  <tr><td className="mono">v1</td><td>2025-08-19</td><td>jamie</td><td className="mono">#1018</td><td>1 of 1</td><td><Badge kind="ok">initial</Badge></td></tr>
                </tbody>
              </table>
              <Ann>each migration = explicit operator action. nothing is automatic.</Ann>
            </div>
          </div>

          <div className="col">
            <div className="card" style={{gap:6}}>
              <h3>Quick facts</h3>
              <dl className="kv">
                <dt>Source</dt><dd>One-time migration</dd>
                <dt>Refresh</dt><dd className="muted">— not applicable —</dd>
                <dt>Proxying</dt><dd className="muted">— not supported —</dd>
                <dt>Drift detection</dt><dd className="muted">— n/a —</dd>
                <dt>Credentials stored</dt><dd>None</dd>
                <dt>Materialised</dt><dd className="mono">prod-postgis</dd>
              </dl>
            </div>
            <Callout kind="info">
              <b>Need fresh data?</b> Click <b>Migrate again</b> in the page header. You'll be walked through the same wizard with the source URL pre-filled, and can choose: overwrite this resource, or create a new version alongside.
            </Callout>
            <Ann red>this view differs from a Postgres-backed resource: no refresh, no schedule, no schema-drift watcher.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function SettingsCatalogs() {
  return (
    <div className="scr">
      <TopBar crumbs={['Settings','Catalog endpoints']} />
      <Sidebar active="settings" />
      <div className="main">
        <PageHead title="Settings" sub="Server-wide configuration. Most changes take effect immediately; some require a service restart." />
        <div style={{display:'grid', gridTemplateColumns:'180px 1fr', flex:1, overflow:'hidden'}}>
          {/* sub-nav */}
          <div style={{borderRight:'1px solid #e4e4e4', padding:'10px 0', background:'#fafafa', fontSize:11.5}}>
            {[
              { k:'Access', g:'Govern' },
              { k:'Auth providers', g:'Govern' },
              { k:'CORS', g:'Govern' },
              { k:'API keys', g:'Govern' },
              { k:'License', g:'Server' },
              { k:'About & version', g:'Server' },
              { k:'Map preview', g:'Server' },
              { k:'Catalog endpoints', g:'Server', on:true },
              { k:'Feature flags', g:'Server' },
              { k:'Webhooks', g:'Integrations' },
              { k:'Notifications', g:'Integrations' },
              { k:'Audit log', g:'Integrations' },
            ].reduce((acc, it) => {
              if (!acc.length || acc[acc.length-1].g !== it.g) acc.push({ g:it.g, items:[] });
              acc[acc.length-1].items.push(it);
              return acc;
            }, []).map((g,i) => (
              <div key={i}>
                <div style={{padding:'8px 14px 4px', fontSize:9.5, textTransform:'uppercase', letterSpacing:'0.08em', color:'#888'}}>{g.g}</div>
                {g.items.map(it => (
                  <div key={it.k} style={{
                    padding:'4px 14px', height:24,
                    background: it.on ? 'var(--accent)' : 'transparent',
                    fontWeight: it.on ? 600 : 400,
                    borderLeft: it.on ? '3px solid #141414' : '3px solid transparent',
                    cursor:'pointer',
                  }}>{it.k}</div>
                ))}
              </div>
            ))}
          </div>

          {/* content */}
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Catalog endpoints</h2>
            <div className="muted" style={{fontSize:11.5, marginBottom:14}}>
              Whether Honua exposes each kind of catalog at all. When a catalog is OFF, the "Register in catalog" checkbox on every service is disabled. Already-registered entries become unreachable until you switch it back on — service URLs themselves are unaffected.
            </div>

            <div className="col" style={{gap:10}}>
              {[
                { k:'esri', t:'Esri catalog',
                  d:'Discovery endpoint for FeatureServer / MapServer / ImageServer publications.',
                  on:true, url:'/catalog', n:38, anon:true, auto:true,
                  feeds:'GeoServices FeatureServer · GeoServices MapServer · ImageServer',
                  note:'Auto-default: every Esri service publication is registered here unless explicitly unchecked.' },
                { k:'ogc',  t:'OGC API Records',
                  d:'Discovery endpoint for OGC API Features publications. Open standard.',
                  on:true, url:'/records', n:38, anon:true, auto:true,
                  feeds:'OGC API Features',
                  note:'Auto-default: every OGC API Features publication is registered here unless explicitly unchecked.' },
                { k:'odata', t:'OData catalog',
                  d:'Service document + $metadata letting BI tools discover entity sets.',
                  on:true, url:'/odata', n:3, anon:true, auto:false,
                  feeds:'OData (per entity set)',
                  note:'Opt-in per entity set. Publishing an OData service does not auto-register; check entity sets you want public.' },
                { k:'stac', t:'STAC',
                  d:'SpatioTemporal Asset Catalog — raster-leaning. Opt-in per resource.',
                  on:false, url:'/stac', n:0, anon:true, auto:false,
                  feeds:'per resource',
                  note:'Opt-in per resource. Check "Register in STAC" on a resource to publish a collection here.' },
                { k:'dcat', t:'DCAT',
                  d:'Open-data catalog — national / EU portals. Opt-in per resource.',
                  on:false, url:'/dcat', n:0, anon:true, auto:false,
                  feeds:'per resource',
                  note:'Opt-in per resource. Check "Register in DCAT" on a resource to publish a dataset entry here.' },
              ].map(c => (
                <div key={c.k} style={{
                  border:'1.2px solid', borderColor: c.on ? 'var(--ink)' : '#d8d8d8',
                  borderRadius:6,
                  background: c.on ? '#fff' : '#fafafa',
                  padding:0,
                }}>
                  <div style={{padding:'10px 14px', borderBottom:'1px solid #eee', display:'flex', alignItems:'center', gap:10}}>
                    <span style={{fontSize:16, color:'#666'}}>▤</span>
                    <div style={{flex:1}}>
                      <div className="row" style={{gap:6}}>
                        <b style={{fontSize:13}}>{c.t}</b>
                        {c.on ? <Badge kind="ok">ON</Badge> : <Badge>OFF</Badge>}
                      </div>
                      <div className="muted" style={{fontSize:11, marginTop:2}}>{c.d}</div>
                    </div>
                    {/* default-on/opt-in indicator */}
                    {c.auto
                      ? <Badge kind="accent" style={{marginRight:6}}>auto-default</Badge>
                      : <Badge style={{marginRight:6}}>opt-in</Badge>}
                    {/* toggle */}
                    <div style={{
                      width:46, height:24, borderRadius:12, position:'relative',
                      background: c.on ? 'var(--accent-deep)' : '#ccc',
                      border:'1.2px solid var(--ink)', cursor:'pointer',
                    }}>
                      <span style={{
                        position:'absolute', top:1,
                        left: c.on ? 22 : 1,
                        width:20, height:20, borderRadius:'50%', background:'#fff',
                        border:'1px solid var(--ink)',
                      }} />
                    </div>
                  </div>

                  {c.on ? (
                    <>
                      <div style={{padding:'10px 14px', display:'grid', gridTemplateColumns:'160px 1fr', rowGap:6, columnGap:10, fontSize:11.5}}>
                        <span className="muted">Endpoint URL</span>
                        <div className="row" style={{gap:6}}>
                          <code className="mono" style={{flex:1, background:'#fafafa', border:'1px solid #e4e4e4', padding:'2px 6px', borderRadius:3, fontSize:11}}>
                            https://honua.example.gov{c.url}
                          </code>
                          <Btn ghost sm>⧉ Copy</Btn>
                          <Btn sm>Open ↗</Btn>
                        </div>

                        <span className="muted">Entries published</span>
                        <span><b>{c.n}</b> <span className="muted">from {c.feeds.split(' · ').length} service kind{c.feeds.includes('·') ? 's' : ''}</span></span>

                        <span className="muted">Anonymous discovery</span>
                        <span>{c.anon ? <Badge kind="accent">yes</Badge> : <Badge>auth required</Badge>}</span>

                        <span className="muted">Fed by</span>
                        <span className="mono" style={{fontSize:10.5}}>{c.feeds}</span>
                      </div>
                      <div style={{padding:'6px 14px 10px', borderTop:'1px dashed #eee', display:'flex', alignItems:'center', gap:6}}>
                        <Btn ghost sm>Edit metadata defaults</Btn>
                        <Btn ghost sm>CORS for this catalog</Btn>
                        <Btn ghost sm>Rebuild index</Btn>
                        <div style={{flex:1}}/>
                        <span className="muted" style={{fontSize:10.5}}>last rebuild 14m ago · healthy</span>
                      </div>
                    </>
                  ) : (
                    <div style={{padding:'10px 14px', fontSize:11.5}}>
                      <Callout kind="info" style={{marginBottom:0}}>
                        <b>Endpoint is off.</b> Consumers cannot reach <span className="mono">{c.url}</span>. Per-service "Register in catalog" checkboxes for this kind are disabled until you turn it back on.
                        {c.note && <div style={{marginTop:4, color:'#666'}}>{c.note}</div>}
                      </Callout>
                    </div>
                  )}
                </div>
              ))}
            </div>

            <Callout kind="info" style={{marginTop:14}}>
              <b>Auto-default vs opt-in.</b> Esri catalog and OGC API Records auto-register every matching service publication (checkbox is pre-checked, operator can uncheck). OData catalog, STAC, and DCAT are opt-in — nothing is registered until you explicitly check the box per entity set / resource.
            </Callout>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { Settings, SettingsCatalogs, ResSource, ResSourceImported, ResPresentation, ResPresentationPopups, ResAdvanced, StatesGallery });
