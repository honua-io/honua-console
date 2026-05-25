// Styling surfaces:
//   1. Resource → Presentation → Styles (CANONICAL · MapLibre via Maputnik) — already in screens-settings-states.jsx
//   2. Resource → Presentation → Style endpoint (OGC API Styles · all encodings via content-neg) — NEW here
//   3. Service Slot → Styling override (per-slot Esri renderer override + drift status) — NEW here

function ResStyleEndpoint() {
  // Resource-level OGC API Styles endpoint. One source of truth, many encodings.
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
            {['Styles','Style endpoint','Labels','Popups','Relationships','Events','History'].map((t,i) => (
              <div key={t} style={{
                padding:'6px 12px',
                background: i === 1 ? 'var(--accent)' : 'transparent',
                borderLeft: i === 1 ? '3px solid var(--ink)' : '3px solid transparent',
                fontWeight: i === 1 ? 600 : 400, cursor:'pointer',
              }}>{t}</div>
            ))}
            <div className="muted" style={{padding:'12px 12px', fontSize:10.5, borderTop:'1px dashed #d8d8d8', marginTop:6}}>
              <b>Canonical:</b> MapLibre GL.<br/>
              SLD &amp; Esri Renderer are <i>generated</i> build artefacts.<br/>
              Slot overrides are explicit.
            </div>
          </div>

          {/* content */}
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Style endpoint</h2>
            <div className="muted" style={{fontSize:11.5, marginBottom:14}}>
              OGC API Styles endpoint for this resource. One canonical MapLibre GL style on the left; every encoding consumers might ask for is served from here via content negotiation.
            </div>

            {/* Endpoint URL */}
            <div className="card" style={{marginBottom:12}}>
              <div className="row" style={{marginBottom:6}}>
                <h3 style={{flex:1}}>Endpoint</h3>
                <Badge kind="ok">live</Badge>
              </div>
              <div style={{display:'grid', gridTemplateColumns:'140px 1fr', rowGap:6, columnGap:10, fontSize:11.5}}>
                <span className="muted">Style URL</span>
                <div className="row" style={{gap:6}}>
                  <code className="mono" style={{flex:1, background:'#fafafa', border:'1px solid #e4e4e4', padding:'2px 6px', borderRadius:3, fontSize:11}}>
                    https://honua.example.gov/resources/parcels_2024/styles/default
                  </code>
                  <Btn ghost sm>⧉ Copy</Btn>
                  <Btn sm>Open ↗</Btn>
                </div>

                <span className="muted">Style version</span>
                <span><b>v4</b> <span className="muted">· last edited 2h ago · jamie · auto-published when canonical changes</span></span>

                <span className="muted">Used by slots</span>
                <span className="mono" style={{fontSize:10.5}}>6 service layer slots</span>
              </div>
            </div>

            {/* Encodings */}
            <div className="card" style={{padding:0, marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <div className="row">
                  <h3 style={{flex:1}}>Encodings · content negotiation</h3>
                  <span className="muted" style={{fontSize:11}}>same style, served in each format on request</span>
                </div>
                <div className="muted" style={{fontSize:11, marginTop:2}}>
                  All encodings except MapLibre are <b>generated build artefacts</b>. They regenerate on every canonical change. Don't author SLD or Esri Renderer here — use a per-slot override if you need to.
                </div>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th>Encoding</th>
                  <th>Media type</th>
                  <th>Source</th>
                  <th>Status</th>
                  <th>Size</th>
                  <th style={{width:160}}></th>
                </tr></thead>
                <tbody>
                  <tr style={{background:'#fffae0'}}>
                    <td><b>MapLibre GL</b> <Badge kind="accent" style={{marginLeft:4}}>canonical</Badge></td>
                    <td className="mono">application/vnd.mapbox.style+json</td>
                    <td>authored in Maputnik</td>
                    <td><Badge kind="ok">live · v4</Badge></td>
                    <td className="mono">14 KB</td>
                    <td>
                      <div className="row" style={{gap:4, fontSize:10.5}}>
                        <a style={{cursor:'pointer'}}>Edit ↗</a>
                        <span style={{color:'#ddd'}}>·</span>
                        <a style={{cursor:'pointer'}}>Preview</a>
                        <span style={{color:'#ddd'}}>·</span>
                        <a style={{cursor:'pointer'}}>⧉ Copy</a>
                      </div>
                    </td>
                  </tr>
                  <tr>
                    <td>SLD / SE</td>
                    <td className="mono">application/vnd.ogc.sld+xml</td>
                    <td className="muted">generated from canonical</td>
                    <td><Badge kind="ok">in sync · v4</Badge></td>
                    <td className="mono">38 KB</td>
                    <td>
                      <div className="row" style={{gap:4, fontSize:10.5}}>
                        <a style={{cursor:'pointer'}}>View</a>
                        <span style={{color:'#ddd'}}>·</span>
                        <a style={{cursor:'pointer'}}>⧉ Copy</a>
                      </div>
                    </td>
                  </tr>
                  <tr>
                    <td>Esri Renderer JSON</td>
                    <td className="mono">application/json; profile=esri-renderer</td>
                    <td className="muted">generated from canonical</td>
                    <td><Badge kind="ok">in sync · v4</Badge></td>
                    <td className="mono">6 KB</td>
                    <td>
                      <div className="row" style={{gap:4, fontSize:10.5}}>
                        <a style={{cursor:'pointer'}}>View</a>
                        <span style={{color:'#ddd'}}>·</span>
                        <a style={{cursor:'pointer'}}>⧉ Copy</a>
                      </div>
                    </td>
                  </tr>
                  <tr>
                    <td>QGIS QML (sidecar)</td>
                    <td className="mono">application/x-qgis-qml+xml</td>
                    <td className="muted">generated from canonical</td>
                    <td><Badge>experimental</Badge></td>
                    <td className="mono">12 KB</td>
                    <td><a style={{cursor:'pointer', fontSize:10.5}}>View</a></td>
                  </tr>
                  <tr style={{opacity:0.55}}>
                    <td>3D Tiles style</td>
                    <td className="mono">application/json; profile=3dtiles</td>
                    <td className="muted">— resource is 2D —</td>
                    <td><span className="muted">n/a</span></td>
                    <td className="muted">—</td>
                    <td></td>
                  </tr>
                </tbody>
              </table>
            </div>

            {/* Slots and overrides */}
            <div className="card" style={{padding:0, marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center'}}>
                <h3>Where this style is in use</h3>
                <span className="muted" style={{fontSize:11, marginLeft:8}}>service slots binding this resource · per-slot styling status</span>
                <div style={{flex:1}}/>
                <Btn ghost sm>Resync all to canonical</Btn>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th>Service slot</th><th>Encoding served</th><th>Status</th><th>Last canonical change</th><th></th>
                </tr></thead>
                <tbody>
                  <tr>
                    <td className="mono">public-works-fs / layer 0</td>
                    <td>Esri Renderer JSON</td>
                    <td><Badge kind="ok">tracking canonical</Badge></td>
                    <td className="muted">2h ago · propagated</td>
                    <td><a style={{fontSize:10.5,cursor:'pointer'}}>Open slot ↗</a></td>
                  </tr>
                  <tr>
                    <td className="mono">public-works-ms / layer 2</td>
                    <td>Esri Renderer JSON</td>
                    <td><Badge kind="warn">override active</Badge></td>
                    <td className="muted">2h ago · not propagated</td>
                    <td><a style={{fontSize:10.5,cursor:'pointer'}}>Open slot ↗</a></td>
                  </tr>
                  <tr>
                    <td className="mono">features-public / parcels_2024</td>
                    <td>MapLibre GL (link)</td>
                    <td><Badge kind="ok">tracking canonical</Badge></td>
                    <td className="muted">2h ago · live</td>
                    <td><a style={{fontSize:10.5,cursor:'pointer'}}>Open slot ↗</a></td>
                  </tr>
                  <tr>
                    <td className="mono">tiles-public / parcels_2024</td>
                    <td>MapLibre GL (vector tiles)</td>
                    <td><Badge kind="warn">canonical changed · needs rebuild</Badge></td>
                    <td className="muted">2h ago · tile cache 3d old</td>
                    <td><div className="row" style={{gap:4}}><a style={{fontSize:10.5,cursor:'pointer'}}>Rebuild tiles</a><span style={{color:'#ddd'}}>·</span><a style={{fontSize:10.5,cursor:'pointer'}}>Open slot</a></div></td>
                  </tr>
                  <tr>
                    <td className="mono">fs-internal / layer 4</td>
                    <td>Esri Renderer JSON</td>
                    <td><Badge kind="warn">override active · resync available</Badge></td>
                    <td className="muted">2h ago · diverged on v3</td>
                    <td><div className="row" style={{gap:4}}><a style={{fontSize:10.5,cursor:'pointer',color:'var(--pencil)'}}>Resync</a><span style={{color:'#ddd'}}>·</span><a style={{fontSize:10.5,cursor:'pointer'}}>Open slot</a></div></td>
                  </tr>
                </tbody>
              </table>
            </div>

            <Callout kind="info">
              <b>Override semantics.</b> A slot can stay <i>tracking canonical</i> (default), or be <i>overridden</i> with a hand-authored style in its native encoding. Overrides have provenance — you can always see the canonical they diverged from and resync if you want.
            </Callout>

            <Ann red>generated artefacts (SLD, Esri Renderer JSON) live in the build cache. they're disposable.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function SlotStylingOverride() {
  // Service Slot detail · Styling sub-tab. Esri FeatureServer slot.
  // Shows: canonical preview vs translated, with optional override.
  return (
    <div className="scr">
      <TopBar crumbs={['Services & layers','public-works-ms','layer 2 · Parcels']} />
      <Sidebar active="services" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>public / public-works-ms / layer 2</div>
          <div className="row" style={{marginTop:2}}>
            <h1 style={{margin:0, font:'600 18px var(--ui)'}}>Parcels</h1>
            <span className="tag">MapServer · layer 2</span>
            <Badge kind="ok">Live · v4</Badge>
            <Badge kind="warn">style override active</Badge>
            <div style={{flex:1}}/>
            <Btn ghost>⧉ Copy URL</Btn>
            <Btn>🗺 Map preview</Btn>
            <Btn kind="p">↗ Open Data Resource</Btn>
          </div>
          <div className="muted" style={{fontSize:11.5, marginTop:4}}>
            Layer slot backed by <a className="mono" style={{color:'var(--pencil)'}}>◇ parcels_2024</a>. Style for this slot has been hand-overridden — Esri renderer is using arcade expressions that MapLibre can't represent.
          </div>
        </div>

        <Tabs items={[
          { k:'overview', t:'Overview' },
          { k:'fields', t:'Field exposure' },
          { k:'styling', t:'Styling' },
          { k:'access', t:'Access' },
          { k:'validation', t:'Validation' },
        ]} active="styling" />

        {/* Status bar */}
        <div style={{padding:'10px 18px', background:'#fff7e6', borderBottom:'1px solid #e7c97a', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="warn">override active</Badge>
          <div style={{flex:1}}>
            <b>This slot's style was forked at canonical v3.</b> The resource canonical advanced to v4 (2h ago) — your override didn't pick up those changes. You can resync (loses your override) or keep diverged.
          </div>
          <Btn sm>Diff against canonical</Btn>
          <Btn ghost sm>Resync to v4 (lose override)</Btn>
          <Btn kind="p" sm>Keep override</Btn>
        </div>

        <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', flex:1, overflow:'hidden'}}>
          {/* LEFT: canonical (read-only here) */}
          <div style={{display:'flex', flexDirection:'column', borderRight:'1px solid #e4e4e4'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', display:'flex', alignItems:'center', gap:8}}>
              <span style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', color:'#888'}}>Canonical · MapLibre GL</span>
              <Badge kind="accent">v4 · 2h ago</Badge>
              <div style={{flex:1}}/>
              <a style={{fontSize:10.5, color:'var(--pencil)', cursor:'pointer'}}>Edit on resource ↗</a>
            </div>

            <div style={{padding:10, background:'#fafafa', borderBottom:'1px solid #e4e4e4'}}>
              <MapPreview mode="layer" height={220} popup={false} scaleText="1:8,000" />
            </div>

            <div style={{padding:'8px 12px', fontSize:10.5, color:'#666'}}>
              Auto-translates to Esri Renderer JSON:
            </div>
            <pre className="mono" style={{margin:0,padding:'8px 12px', background:'#0e0e0e', color:'#d8d8d8', fontSize:10.5, lineHeight:1.55, flex:1, overflow:'auto', whiteSpace:'pre-wrap'}}>
{`{
  "type": "classBreaks",
  "field": "area_m2",
  "minValue": 0,
  "classBreakInfos": [
    { "classMaxValue": 740,    "symbol": { "color": [247,244,232] } },
    { "classMaxValue": 1420,   "symbol": { "color": [234,215,138] } },
    { "classMaxValue": 2140,   "symbol": { "color": [217,162,58]  } },
    { "classMaxValue": 3920,   "symbol": { "color": [181,107,28]  } },
    { "classMaxValue": null,   "symbol": { "color": [ 97, 45,10]  } }
  ]
}`}
            </pre>
            <div style={{padding:'6px 12px', borderTop:'1px solid #e4e4e4', background:'#fafafa', fontSize:10.5, color:'#666'}}>
              <b>What the override added</b> (not expressible in MapLibre): arcade expression on outline color for <span className="mono">use_code = "GOV-SEC"</span>, dot-density backup at zoom &lt; 8.
            </div>
          </div>

          {/* RIGHT: per-slot override editor */}
          <div style={{display:'flex', flexDirection:'column', background:'#1f2329'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #0a0c10', background:'#252a31', color:'#d8d8d8', display:'flex', alignItems:'center', gap:8}}>
              <span style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', color:'#9aa3ad'}}>Slot override · Esri Renderer JSON</span>
              <Badge kind="warn">override</Badge>
              <span className="muted mono" style={{fontSize:10, color:'#6e7682'}}>forked from canonical v3 · 5d ago · jamie</span>
              <div style={{flex:1}}/>
              <a style={{fontSize:10.5, color:'var(--accent)', cursor:'pointer'}}>+ New version</a>
            </div>

            <div style={{padding:10}}>
              <div style={{filter:'invert(1) hue-rotate(180deg)'}}>
                <MapPreview mode="layer" height={220} popup={false} scaleText="1:8,000" />
              </div>
            </div>

            <pre className="mono" style={{margin:0,padding:'8px 12px', background:'#0e0e0e', color:'#d8d8d8', fontSize:10.5, lineHeight:1.55, flex:1, overflow:'auto', borderTop:'1px solid #0a0c10', whiteSpace:'pre-wrap'}}>
{`{
  "type": "classBreaks",
  "field": "area_m2",
  "classBreakInfos": [ /* … */ ],
  "visualVariables": [
    {
      "type": "colorInfo",
      "valueExpression": "When($feature.use_code == 'GOV-SEC', '#c03b2b', null)",
      "valueExpressionTitle": "Restricted parcels"
    }
  ],
  "backupRenderer": {
    "type": "dotDensity",
    "field": "area_m2",
    "dotValue": 50,
    "outline": null
  }
}`}
            </pre>

            <div style={{padding:'8px 12px', borderTop:'1px solid #0a0c10', background:'#252a31', color:'#d8d8d8', fontSize:10.5}}>
              <div style={{marginBottom:6}}>
                <span style={{color:'#9aa3ad'}}>Why this can't round-trip to canonical:</span>
              </div>
              <ul style={{margin:'0 0 0 16px', padding:0, lineHeight:1.55, color:'#d8d8d8'}}>
                <li><b>Arcade <span className="mono">valueExpression</span></b> — Esri-only DSL. MapLibre uses expressions but not Arcade.</li>
                <li><b><span className="mono">backupRenderer</span></b> with <span className="mono">dotDensity</span> — no MapLibre equivalent.</li>
              </ul>
            </div>

            <div style={{padding:'6px 12px', borderTop:'1px solid #0a0c10', background:'#1a1d22', display:'flex', alignItems:'center', gap:6}}>
              <button style={{padding:'4px 10px', background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:10.5, cursor:'pointer'}}>Format JSON</button>
              <button style={{padding:'4px 10px', background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:10.5, cursor:'pointer'}}>Import from FeatureServer URL</button>
              <div style={{flex:1}}/>
              <button style={{padding:'4px 10px', background:'#1a1d22', border:'1px solid #5a2a26', color:'#e07765', borderRadius:4, fontSize:10.5, cursor:'pointer'}}>Discard override · track canonical</button>
              <button style={{padding:'4px 10px', background:'var(--accent-deep)', border:'1px solid var(--accent-deep)', color:'#141414', borderRadius:4, fontSize:10.5, cursor:'pointer', fontWeight:600}}>Save override</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ResStyleEndpoint, SlotStylingOverride });
