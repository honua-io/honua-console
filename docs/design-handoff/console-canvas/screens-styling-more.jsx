// More styling surfaces — built after canonical + Esri override + style endpoint.
//   StyleVersionHistory  — versioned timeline of canonical MapLibre style with diff view
//   SlotStylingWMS       — per-slot SLD override editor for a WMS slot
//   ResyncConfirm        — modal: "Resync to v4 — your override will be lost" with diff preview

function StyleVersionHistory() {
  // Resource → Presentation → Styles · "History" tab
  // Timeline of canonical MapLibre style versions + diff between any two.
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024','Presentation','Style history']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="define" sub="presentation" />

        <div style={{display:'grid', gridTemplateColumns:'200px 1fr', flex:1, overflow:'hidden'}}>
          {/* sub-nav with History */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', padding:'8px 0', fontSize:11.5}}>
            {['Styles','Style endpoint','History','Labels','Popups','Relationships','Events'].map((t,i) => (
              <div key={t} style={{
                padding:'6px 12px',
                background: i === 2 ? 'var(--accent)' : 'transparent',
                borderLeft: i === 2 ? '3px solid var(--ink)' : '3px solid transparent',
                fontWeight: i === 2 ? 600 : 400, cursor:'pointer',
              }}>{t}</div>
            ))}
            <div className="muted" style={{padding:'12px 12px', fontSize:10.5, borderTop:'1px dashed #d8d8d8', marginTop:6}}>
              Auto-versions on every Maputnik save. Explicit publish required for slots to pick up changes.
            </div>
          </div>

          {/* content: timeline + diff */}
          <div style={{display:'grid', gridTemplateColumns:'380px 1fr', overflow:'hidden'}}>
            {/* TIMELINE */}
            <div style={{borderRight:'1px solid #e4e4e4', overflow:'auto'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fafafa'}}>
                <div className="row">
                  <h3 style={{flex:1}}>Versions</h3>
                  <Btn ghost sm>Filter</Btn>
                </div>
                <div className="muted" style={{fontSize:10.5, marginTop:2}}>auto-save + named published versions</div>
              </div>

              {[
                { v:'v4', t:'2h ago', who:'jamie', kind:'published', sel:true,
                  note:'tightened class break thresholds; added zoom 14 expression',
                  slots:'public-works-fs/0, features-public/parcels_2024, fs-internal/4',
                  unsync:1 },
                { v:'v4-draft.3', t:'2h ago', who:'jamie', kind:'auto-save', sel:false,
                  note:'fill-opacity 0.85 → 0.88' },
                { v:'v4-draft.2', t:'3h ago', who:'jamie', kind:'auto-save' },
                { v:'v4-draft.1', t:'3h ago', who:'jamie', kind:'auto-save' },
                { v:'v3', t:'5d ago', who:'k.tan', kind:'published',
                  note:'introduced PII halo on owner_name layer',
                  slots:'public-works-fs/0, public-works-ms/2, features-public/parcels_2024',
                  override:'public-works-ms/2 hand-forked here · arcade expression added' },
                { v:'v2', t:'3w ago', who:'jamie', kind:'published',
                  note:'class-breaks renderer instead of single-symbol' },
                { v:'v1', t:'4w ago', who:'jamie', kind:'published',
                  note:'initial style · auto-generated from class-breaks defaults' },
              ].map((row, i) => (
                <div key={i} style={{
                  display:'flex', alignItems:'flex-start', gap:8,
                  padding:'8px 12px', borderBottom:'1px solid #f1f1f1',
                  background: row.sel ? '#fffae0' : 'transparent',
                  borderLeft: row.sel ? '3px solid var(--ink)' : '3px solid transparent',
                  cursor:'pointer', position:'relative',
                }}>
                  {/* timeline dot */}
                  <div style={{
                    width:10, height:10, borderRadius:'50%',
                    background: row.kind === 'published' ? 'var(--ink)' : '#fff',
                    border: '2px solid var(--ink)',
                    marginTop:4, flexShrink:0,
                  }}/>
                  <div style={{flex:1, minWidth:0}}>
                    <div className="row" style={{gap:6}}>
                      <span className="mono" style={{fontSize:11.5, fontWeight: row.sel ? 700 : 600}}>{row.v}</span>
                      {row.kind === 'published'
                        ? <Badge kind="ok">published</Badge>
                        : <Badge>auto-save</Badge>}
                      {row.unsync > 0 && <Badge kind="warn">{row.unsync} unsync slot</Badge>}
                      <div style={{flex:1}}/>
                      <span className="muted" style={{fontSize:10}}>{row.t}</span>
                    </div>
                    <div className="muted" style={{fontSize:10.5, marginTop:2}}>by {row.who}</div>
                    {row.note && <div style={{fontSize:11, marginTop:4, color:'#444'}}>{row.note}</div>}
                    {row.slots && (
                      <div style={{fontSize:10, color:'#888', marginTop:3, fontFamily:'var(--mono)'}}>
                        → {row.slots}
                      </div>
                    )}
                    {row.override && (
                      <div style={{fontSize:10, color:'var(--warn)', marginTop:3}}>
                        ⚠ {row.override}
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>

            {/* DIFF VIEWER */}
            <div style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center', gap:8, background:'#fafafa'}}>
                <h3>Compare</h3>
                <div className="row" style={{gap:6, fontSize:11}}>
                  <Sel value="v3 · published · 5d ago" />
                  <span style={{color:'#888'}}>→</span>
                  <Sel value="v4 · published · 2h ago" />
                </div>
                <div style={{flex:1}}/>
                <Btn ghost sm>Swap</Btn>
                <Btn ghost sm>Restore v3</Btn>
                <Btn sm>Republish to all slots</Btn>
              </div>

              {/* Visual diff: side-by-side mini-maps */}
              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:0, borderBottom:'1px solid #e4e4e4'}}>
                <div style={{padding:'8px 12px', background:'#fff'}}>
                  <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>v3 · 5d ago</div>
                  <div style={{filter:'saturate(0.7)'}}>
                    <MapPreview mode="layer" height={180} popup={false} scaleText="1:8,000" />
                  </div>
                </div>
                <div style={{padding:'8px 12px', background:'#fff', borderLeft:'1px dashed #d8d8d8'}}>
                  <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>v4 · 2h ago</div>
                  <MapPreview mode="layer" height={180} popup={false} scaleText="1:8,000" />
                </div>
              </div>

              {/* Text diff */}
              <div style={{padding:'8px 12px', flex:1, overflow:'auto'}}>
                <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>Changes · 4 layer modifications</div>
                <pre className="mono" style={{margin:0,padding:10, background:'#fafafa', border:'1px solid #e4e4e4', borderRadius:4, fontSize:10.5, lineHeight:1.55, whiteSpace:'pre-wrap'}}>
{`  layer "parcels/fill"
    paint:
-     fill-color: ["interpolate", ["linear"], ["get","area_m2"], 0, "#f7f4e8", 4000, "#612d0a"]
+     fill-color: ["interpolate", ["linear"], ["get","area_m2"], 0, "#f7f4e8", 3920, "#612d0a"]
-     fill-opacity: 0.85
+     fill-opacity: 0.88

  layer "parcels/outline"
    paint:
+     line-color: ["case", ["==", ["get","use_code"], "GOV-SEC"], "#c03b2b", "#7a6f55"]
      line-width: 0.7

+ layer "parcels/labels"  (new)
    type: symbol
    minzoom: 13
    layout: { text-field: ["get","parcel_id"], text-size: 10 }
    paint:  { text-color: "#3a2f17", text-halo-color: "#fff", text-halo-width: 1 }`}
                </pre>
              </div>

              <div style={{padding:'8px 12px', borderTop:'1px solid #e4e4e4', background:'#fff7e6', display:'flex',alignItems:'center', gap:8, fontSize:11.5}}>
                <Badge kind="warn">1 slot didn't pick up v4</Badge>
                <span style={{flex:1}}>
                  <span className="mono">fs-internal / layer 4</span> has an override active. Resync from its slot page, or keep diverged.
                </span>
                <Btn sm>Open slot</Btn>
                <Btn kind="p" sm>Resync slot…</Btn>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function SlotStylingWMS() {
  // Service slot → Styling override · WMS (SLD authoring)
  // Default: tracking canonical MapLibre, served as auto-generated SLD.
  // Override: hand-authored SLD that adds raster ColorMap + custom RasterSymbolizer.
  return (
    <div className="scr">
      <TopBar crumbs={['Services & layers','tiles-public','land_cover']} />
      <Sidebar active="services" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>public / tiles-public / land_cover</div>
          <div className="row" style={{marginTop:2}}>
            <h1 style={{margin:0, font:'600 18px var(--ui)'}}>land_cover</h1>
            <span className="tag">WMS · GetMap layer</span>
            <Badge kind="ok">Live · v2</Badge>
            <Badge kind="warn">SLD override active</Badge>
            <div style={{flex:1}}/>
            <Btn ghost>⧉ Copy URL</Btn>
            <Btn>🗺 Map preview</Btn>
            <Btn kind="p">↗ Open Data Resource</Btn>
          </div>
          <div className="muted" style={{fontSize:11.5, marginTop:4}}>
            WMS layer backed by <a className="mono" style={{color:'var(--pencil)'}}>◇ land_cover_2024</a>. Hand-authored SLD adds a per-class ColorMap that MapLibre's canonical style can't fully express for raster.
          </div>
        </div>

        <Tabs items={[
          { k:'overview', t:'Overview' },
          { k:'fields', t:'Field exposure' },
          { k:'styling', t:'Styling · SLD' },
          { k:'access', t:'Access' },
          { k:'validation', t:'Validation' },
        ]} active="styling" />

        <div style={{padding:'10px 18px', background:'#fff7e6', borderBottom:'1px solid #e7c97a', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="warn">override active</Badge>
          <div style={{flex:1}}>
            <b>Forked from canonical v1.</b> Resource canonical advanced to v2 (3d ago) — your hand-authored SLD didn't pick up those changes. Land cover's per-class colour ramp doesn't round-trip to MapLibre style.
          </div>
          <Btn sm>Diff canonical</Btn>
          <Btn ghost sm>Resync to v2 (lose SLD)</Btn>
          <Btn kind="p" sm>Keep override</Btn>
        </div>

        <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', flex:1, overflow:'hidden'}}>
          {/* LEFT: canonical (read-only) */}
          <div style={{display:'flex', flexDirection:'column', borderRight:'1px solid #e4e4e4'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', display:'flex', alignItems:'center', gap:8}}>
              <span style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', color:'#888'}}>Canonical · MapLibre GL</span>
              <Badge kind="accent">v2 · 3d ago</Badge>
              <div style={{flex:1}}/>
              <a style={{fontSize:10.5, color:'var(--pencil)', cursor:'pointer'}}>Edit on resource ↗</a>
            </div>

            <div style={{padding:10, background:'#fafafa', borderBottom:'1px solid #e4e4e4'}}>
              <MapPreview mode="layer" height={200} popup={false} scaleText="1:50,000" />
            </div>

            <div style={{padding:'8px 12px', fontSize:10.5, color:'#666'}}>
              Auto-translates to SLD/SE:
            </div>
            <pre className="mono" style={{margin:0,padding:'8px 12px', background:'#0e0e0e', color:'#d8d8d8', fontSize:10, lineHeight:1.55, flex:1, overflow:'auto', whiteSpace:'pre-wrap'}}>
{`<StyledLayerDescriptor version="1.1.0">
  <NamedLayer><Name>land_cover</Name>
    <UserStyle>
      <FeatureTypeStyle>
        <Rule>
          <RasterSymbolizer>
            <Opacity>0.85</Opacity>
            <ColorMap type="ramp">
              <ColorMapEntry color="#e8f0d0" quantity="0"/>
              <ColorMapEntry color="#7a9c4e" quantity="50"/>
              <ColorMapEntry color="#2e5021" quantity="100"/>
            </ColorMap>
          </RasterSymbolizer>
        </Rule>
      </FeatureTypeStyle>
    </UserStyle>
  </NamedLayer>
</StyledLayerDescriptor>`}
            </pre>
            <div style={{padding:'6px 12px', borderTop:'1px solid #e4e4e4', background:'#fafafa', fontSize:10.5, color:'#666'}}>
              <b>What the override added:</b> 12-class discrete ColorMap (one per NLCD class), per-class label rules, scale-dependent rendering for zoom &lt; 10.
            </div>
          </div>

          {/* RIGHT: hand-authored SLD */}
          <div style={{display:'flex', flexDirection:'column', background:'#1f2329'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #0a0c10', background:'#252a31', color:'#d8d8d8', display:'flex', alignItems:'center', gap:8}}>
              <span style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', color:'#9aa3ad'}}>Slot override · SLD/SE 1.1</span>
              <Badge kind="warn">override</Badge>
              <span className="muted mono" style={{fontSize:10, color:'#6e7682'}}>forked from canonical v1 · 12d ago · jamie</span>
              <div style={{flex:1}}/>
              <a style={{fontSize:10.5, color:'var(--accent)', cursor:'pointer'}}>+ New version</a>
            </div>

            <div style={{padding:10}}>
              <div style={{filter:'invert(1) hue-rotate(180deg) saturate(1.2)'}}>
                <MapPreview mode="layer" height={200} popup={false} scaleText="1:50,000" />
              </div>
            </div>

            <pre className="mono" style={{margin:0,padding:'8px 12px', background:'#0e0e0e', color:'#d8d8d8', fontSize:10, lineHeight:1.55, flex:1, overflow:'auto', borderTop:'1px solid #0a0c10', whiteSpace:'pre-wrap'}}>
{`<StyledLayerDescriptor version="1.1.0">
  <NamedLayer><Name>land_cover</Name>
    <UserStyle>
      <FeatureTypeStyle>
        <Rule>
          <MinScaleDenominator>50000</MinScaleDenominator>
          <RasterSymbolizer>
            <ColorMap type="values">
              <ColorMapEntry color="#5e90ce" quantity="11" label="Open Water"/>
              <ColorMapEntry color="#ddc8c1" quantity="21" label="Developed, Open"/>
              <ColorMapEntry color="#d29b87" quantity="22" label="Developed, Low"/>
              <ColorMapEntry color="#a83815" quantity="23" label="Developed, Medium"/>
              <ColorMapEntry color="#88160a" quantity="24" label="Developed, High"/>
              <ColorMapEntry color="#b0a07d" quantity="31" label="Barren"/>
              <ColorMapEntry color="#697d57" quantity="41" label="Deciduous"/>
              <ColorMapEntry color="#1c5f24" quantity="42" label="Evergreen"/>
              <ColorMapEntry color="#b6cb98" quantity="43" label="Mixed Forest"/>
              <!-- + 3 more classes -->
            </ColorMap>
          </RasterSymbolizer>
        </Rule>
      </FeatureTypeStyle>
    </UserStyle>
  </NamedLayer>
</StyledLayerDescriptor>`}
            </pre>

            <div style={{padding:'8px 12px', borderTop:'1px solid #0a0c10', background:'#252a31', color:'#d8d8d8', fontSize:10.5}}>
              <div style={{marginBottom:6}}>
                <span style={{color:'#9aa3ad'}}>Why this can't round-trip to canonical MapLibre:</span>
              </div>
              <ul style={{margin:'0 0 0 16px', padding:0, lineHeight:1.55}}>
                <li><b>SLD discrete <span className="mono">ColorMap type="values"</span></b> — MapLibre raster styling doesn't support per-pixel class lookups.</li>
                <li><b>Per-class <span className="mono">label</span></b> attributes — drives WMS GetLegendGraphic; no MapLibre equivalent.</li>
              </ul>
            </div>

            <div style={{padding:'6px 12px', borderTop:'1px solid #0a0c10', background:'#1a1d22', display:'flex', alignItems:'center', gap:6}}>
              <button style={{padding:'4px 10px', background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:10.5, cursor:'pointer'}}>Format XML</button>
              <button style={{padding:'4px 10px', background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:10.5, cursor:'pointer'}}>Validate against SE 1.1</button>
              <button style={{padding:'4px 10px', background:'#1a1d22', border:'1px solid #3a4554', color:'#d8d8d8', borderRadius:4, fontSize:10.5, cursor:'pointer'}}>Preview GetLegendGraphic</button>
              <div style={{flex:1}}/>
              <button style={{padding:'4px 10px', background:'#1a1d22', border:'1px solid #5a2a26', color:'#e07765', borderRadius:4, fontSize:10.5, cursor:'pointer'}}>Discard · track canonical</button>
              <button style={{padding:'4px 10px', background:'var(--accent-deep)', border:'1px solid var(--accent-deep)', color:'#141414', borderRadius:4, fontSize:10.5, cursor:'pointer', fontWeight:600}}>Save override</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResyncConfirm() {
  // Modal-as-screen: confirm dialog for "Resync slot to canonical, losing override"
  return (
    <div className="scr scr--noside" style={{position:'relative', background:'#0e0e0e'}}>
      <TopBar crumbs={['Services & layers','public-works-ms','layer 2','Resync to canonical']} />

      {/* dimmed page underneath (preview of the slot styling page) */}
      <div style={{
        position:'absolute', top:38, left:0, right:0, bottom:0,
        background:'#f0eee9', opacity:0.35, pointerEvents:'none',
      }} />

      {/* MODAL */}
      <div style={{
        position:'absolute', top:80, left:'50%', transform:'translateX(-50%)',
        width: 680, background:'#fff',
        border:'2px solid var(--ink)', borderRadius:8,
        boxShadow:'0 24px 64px rgba(0,0,0,.32)',
        display:'flex', flexDirection:'column', maxHeight:'82%',
      }}>
        <div style={{padding:'12px 16px', borderBottom:'1px solid #e4e4e4', background:'var(--accent)', display:'flex',alignItems:'center', gap:8}}>
          <span style={{fontSize:14}}>⚠</span>
          <b style={{fontSize:13}}>Resync to canonical v4</b>
          <span className="muted" style={{fontSize:11}}>· this will discard your slot override</span>
          <div style={{flex:1}}/>
          <span style={{cursor:'pointer', fontSize:14}}>×</span>
        </div>

        <div style={{padding:'14px 18px', overflow:'auto'}}>
          {/* Context */}
          <div style={{display:'flex', alignItems:'center', gap:8, marginBottom:14}}>
            <span style={{color:'#666'}}>◈</span>
            <b style={{fontSize:12}}>public-works-ms / layer 2</b>
            <span style={{color:'#bbb'}}>·</span>
            <span className="mono" style={{fontSize:11}}>Parcels</span>
            <span style={{flex:1}}/>
            <Badge kind="warn">override active</Badge>
          </div>

          <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:12, marginBottom:12}}>
            <div className="card" style={{padding:'10px 12px'}}>
              <div className="row" style={{marginBottom:6}}>
                <b style={{fontSize:11.5}}>What you'll lose</b>
                <div style={{flex:1}}/>
                <Badge kind="warn">your work</Badge>
              </div>
              <ul style={{margin:'0 0 0 16px', padding:0, fontSize:11, lineHeight:1.6, color:'#555'}}>
                <li><span className="mono">visualVariables[0]</span> — arcade <span className="mono">When($feature.use_code == 'GOV-SEC', '#c03b2b', null)</span></li>
                <li><span className="mono">backupRenderer</span> — dotDensity (zoom &lt; 8)</li>
                <li>Forked from canonical v3 · 5d ago by jamie</li>
              </ul>
            </div>
            <div className="card" style={{padding:'10px 12px', background:'#fffae0'}}>
              <div className="row" style={{marginBottom:6}}>
                <b style={{fontSize:11.5}}>What you'll get</b>
                <div style={{flex:1}}/>
                <Badge kind="accent">canonical v4</Badge>
              </div>
              <ul style={{margin:'0 0 0 16px', padding:0, fontSize:11, lineHeight:1.6, color:'#555'}}>
                <li>Class-breaks renderer on <span className="mono">area_m2</span> (5 stops)</li>
                <li>Updated colour ramp · tighter thresholds</li>
                <li>Will track future canonical changes automatically</li>
              </ul>
            </div>
          </div>

          {/* Visual diff */}
          <div className="card" style={{padding:0, marginBottom:12}}>
            <div style={{padding:'6px 12px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', fontSize:10.5, color:'#888', textTransform:'uppercase', letterSpacing:'0.06em'}}>
              Preview
            </div>
            <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:0}}>
              <div style={{padding:'8px 12px', borderRight:'1px dashed #d8d8d8'}}>
                <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Current (with override)</div>
                <MapPreview mode="layer" height={140} popup={false} scaleText="1:8,000" />
              </div>
              <div style={{padding:'8px 12px'}}>
                <div className="muted" style={{fontSize:10.5, marginBottom:4}}>After resync</div>
                <div style={{filter:'saturate(0.9)'}}>
                  <MapPreview mode="layer" height={140} popup={false} scaleText="1:8,000" />
                </div>
              </div>
            </div>
          </div>

          {/* Backup option */}
          <Callout kind="info" style={{marginBottom:10}}>
            <label className="row" style={{gap:6, cursor:'pointer', fontSize:11.5}}>
              <input type="checkbox" readOnly defaultChecked />
              <span><b>Save my override as a named draft first</b> (recommended) — you can restore it later from style history</span>
            </label>
          </Callout>

          {/* Confirm text */}
          <div style={{padding:'8px 10px', background:'#fbeae7', border:'1px solid #e7a59c', borderRadius:4, fontSize:11, color:'#74221a', marginBottom:10}}>
            Type <span className="mono"><b>parcels-ms-2</b></span> to confirm:
            <input readOnly className="inp" style={{marginTop:4, fontFamily:'var(--mono)', width:200}} value="" placeholder="slot identifier" />
          </div>

          <div className="muted" style={{fontSize:10.5}}>
            This action takes effect immediately. Service consumers will see the new style on next cache invalidation (within 30 min for this service).
          </div>
        </div>

        <div style={{padding:'10px 16px', borderTop:'1px solid #e4e4e4', display:'flex', gap:8, background:'#fafafa'}}>
          <Btn ghost>Cancel</Btn>
          <div style={{flex:1}}/>
          <Btn>Keep override</Btn>
          <Btn kind="p" style={{background:'var(--bad)', borderColor:'var(--bad)'}}>Resync · discard override</Btn>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { StyleVersionHistory, SlotStylingWMS, ResyncConfirm });
