// Publish flow: conceptual diagram + step-by-step wizard

function PublishFlowMap() {
  return (
    <div style={{padding:'20px 24px', overflow:'auto', height:'100%', background:'#fcfcfa', position:'relative', fontFamily:'Inter, system-ui, sans-serif'}}>
      <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Publish flow · conceptual</h2>
      <div className="muted" style={{fontSize:11.5, marginBottom:16}}>
        How one canonical Data Resource gets exposed via service slots and catalog entries. Esri services → Esri catalog and OGC API → OGC Records are <b>opt-in checkboxes, default on</b>. STAC and DCAT publishing live in Honua Console.
      </div>

      <div style={{position:'relative', minHeight: 720}}>
        {/* LEFT — the resource */}
        <div style={{position:'absolute', left: 20, top: 30, width: 280}}>
          <div style={{
            border:'2px solid var(--ink)', borderRadius:8, background:'var(--accent)',
            padding:'10px 14px', font:'600 13px var(--ui)'
          }}>
            ◇ Data Resource · parcels_2024
          </div>
          <div style={{
            border:'1px solid #d0d0d0', borderTop:'none', borderRadius:'0 0 8px 8px',
            background:'#fff', padding:'10px 14px', fontSize:11, lineHeight:1.55
          }}>
            <div className="muted" style={{textTransform:'uppercase',fontSize:9.5,letterSpacing:'0.08em',marginBottom:4}}>Canonical (owned here, edited once)</div>
            <div>• Source binding (Postgres / S3 / Esri / file)</div>
            <div>• Fields &amp; semantic roles</div>
            <div>• Metadata (ISO 19115, DCAT, STAC mappings)</div>
            <div>• Access defaults</div>
            <div>• Validation</div>
            <div>• Presentation defaults</div>
          </div>
          <div className="hand" style={{marginTop:8, font:'13px var(--hand)', color:'var(--pencil)'}}>
            ↑ one home for "what this thing is"
          </div>
        </div>

        {/* MIDDLE — the publish action */}
        <div style={{position:'absolute', left: 332, top: 60, width: 320}}>
          <div style={{font:'600 11px var(--ui)', textTransform:'uppercase', letterSpacing:'0.1em', color:'#888', marginBottom:6}}>publish action · 7 steps</div>
          <div style={{
            border:'1.5px dashed var(--ink)', borderRadius:8, padding:'10px 14px',
            background:'#fffdf3', fontSize:11.5, lineHeight:1.7
          }}>
            <div><b>1.</b> Pick target <i>folder / service</i></div>
            <div><b>2.</b> See compatibility (what each format can carry)</div>
            <div><b>3.</b> Configure the <i>layer slot</i> · ID, name, route</div>
            <div><b>4.</b> Choose field exposure &amp; aliases</div>
            <div><b>5.</b> Preview metadata projection</div>
            <div><b>6.</b> Confirm access impact</div>
            <div><b>7.</b> Validate &amp; publish (or save draft)</div>
          </div>
          <Ann red style={{marginTop:8}}>nothing here re-defines the resource. only how it appears in this slot.</Ann>
        </div>

        {/* RIGHT — the slot targets */}
        <div style={{position:'absolute', left: 690, top: 30, width: 340}}>
          <div style={{font:'600 11px var(--ui)', textTransform:'uppercase', letterSpacing:'0.1em', color:'#888', marginBottom:6}}>publications (two kinds)</div>

          <div className="muted" style={{fontSize:10, marginBottom:4, fontWeight:600, textTransform:'uppercase', letterSpacing:'0.06em'}}>Service slots · live endpoints</div>
          {[
            { f:'FeatureServer · /pw/FeatureServer/0',  s:'Parcels',         live:true,  mirror:'esri' },
            { f:'MapServer · /pw/MapServer/2',          s:'Parcels',         live:true,  mirror:'esri' },
            { f:'OGC API Features · /collections/parcels_2024', s:'parcels_2024', live:true, mirror:'ogc' },
            { f:'WMTS · /tiles-public/parcels_2024',    s:'parcels_2024',    live:false, mirror:null },
          ].map((t, i) => (
            <div key={i} style={{
              border:'1px solid #d0d0d0', borderRadius:6, padding:'6px 10px',
              background:'#fff', marginBottom:5, display:'flex', alignItems:'center', gap:8
            }}>
              <span style={{width:8,height:8,borderRadius:'50%',background: t.live ? 'var(--ok)' : 'var(--warn)'}} />
              <div style={{flex:1,fontSize:11}}>
                <div className="mono" style={{fontSize:10.5}}>{t.f}</div>
                <div className="muted" style={{fontSize:10}}>slot label: {t.s}</div>
              </div>
              {t.mirror === 'esri' && <Badge kind="info">↪ Esri catalog</Badge>}
              {t.mirror === 'ogc' && <Badge kind="info">↪ OGC Records</Badge>}
            </div>
          ))}

          <div className="muted" style={{fontSize:10, margin:'10px 0 4px', fontWeight:600, textTransform:'uppercase', letterSpacing:'0.06em'}}>Catalog records · metadata</div>
          {[
            { f:'Esri catalog entry',                 path:'catalog / item / a3bf…0214', kind:'auto-mirror' },
            { f:'OGC Records',                         path:'records-public / parcels_2024', kind:'auto-mirror' },
            { f:'STAC collection',                     path:'stac-public / parcels_2024', kind:'explicit' },
            { f:'DCAT dataset',                        path:'dcat-eu / parcels_2024', kind:'explicit' },
          ].map((t, i) => (
            <div key={i} style={{
              border:'1px solid #d0d0d0', borderRadius:6, padding:'6px 10px',
              background:'#fff', marginBottom:5, display:'flex', alignItems:'center', gap:8
            }}>
              <span style={{width:8,height:8,borderRadius:2,background: t.kind === 'auto-mirror' ? 'var(--pencil)' : 'var(--accent-deep)'}} />
              <div style={{flex:1,fontSize:11}}>
                <div style={{fontSize:11}}>{t.f}</div>
                <div className="muted mono" style={{fontSize:10}}>{t.path}</div>
              </div>
              {t.kind === 'auto-mirror' ? <Badge kind="info">auto</Badge> : <Badge kind="accent">explicit</Badge>}
            </div>
          ))}
        </div>

        {/* SVG arrows */}
        <svg width="100%" height="540" viewBox="0 0 1080 540" style={{position:'absolute', inset:0, pointerEvents:'none'}}>
          {/* L → M */}
          <path d="M 305 100 Q 320 100 332 100" stroke="#141414" strokeWidth="1.5" fill="none" markerEnd="url(#arrow)" />
          {/* M → R (fan-out) */}
          {[60, 110, 160, 210].map((y,i) => (
            <path key={i} d={`M 656 200 Q 670 ${y+30} 690 ${y+30}`} stroke="#bbb" strokeWidth="1" fill="none" strokeDasharray="3 3" markerEnd="url(#arrow-soft)" />
          ))}
          {/* Auto-mirror dotted curves: service slot → catalog mirror */}
          {[
            { from: 85,  to: 290 },  // FS → Esri catalog
            { from: 130, to: 290 },  // MS → Esri catalog (same record)
            { from: 175, to: 335 },  // OGC API → OGC Records
          ].map((m, i) => (
            <path key={'mir'+i} d={`M 1010 ${m.from} C 1080 ${m.from}, 1080 ${m.to}, 1010 ${m.to}`} stroke="var(--pencil)" strokeWidth="1.2" fill="none" strokeDasharray="2 3" markerEnd="url(#arrow-mirror)" />
          ))}
          <defs>
            <marker id="arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
              <path d="M 0 0 L 10 5 L 0 10 z" fill="#141414" />
            </marker>
            <marker id="arrow-soft" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
              <path d="M 0 0 L 10 5 L 0 10 z" fill="#bbb" />
            </marker>
            <marker id="arrow-mirror" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
              <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--pencil)" />
            </marker>
          </defs>
        </svg>

        {/* bottom rules */}
        <div style={{position:'absolute', left:20, top:520, right:20}}>
          <div style={{borderTop:'1.2px dashed #888', paddingTop:14, marginTop:8, display:'grid', gridTemplateColumns:'repeat(3, 1fr)', gap:14}}>
            <div className="card">
              <h3>Two kinds of publication</h3>
              <div style={{fontSize:11.5,lineHeight:1.55}}>
                <div><b>Service slot</b> · runtime endpoint that serves data (FeatureServer, OGC API, WMTS…).</div>
                <div><b>Catalog record</b> · metadata <i>about</i> a resource, with distribution links to its service slots.</div>
              </div>
            </div>
            <div className="card">
              <h3>Catalog registration</h3>
              <div style={{fontSize:11.5,lineHeight:1.55}}>
                When you publish to an <b>Esri service</b>, a "Register in Esri catalog" checkbox is offered, default checked. Same for <b>OGC API Features</b> → <b>OGC Records</b>. Uncheck if you want the service live but hidden from catalog discovery. STAC and DCAT live in Honua Console.
              </div>
            </div>
            <div className="card">
              <h3>Where edits go</h3>
              <div style={{fontSize:11.5,lineHeight:1.55}}>
                <div><b>Resource</b> · meaning, fields, metadata, access defaults.</div>
                <div><b>Slot</b> · layer ID, name, aliases, exposure, cache TTL.</div>
                <div><b>Catalog entry</b> · follows the service automatically while opted in.</div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function WizPublish() {
  // Step 5: map-led projection preview. JSON & metadata are secondary tabs.
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Data resources','parcels_2024','Publish']} />
      <div className="wiz">
        <Stepper steps={['Target','Compatibility','Slot','Fields','Projection','Access','Review']} on={4} />

        {/* Context bar */}
        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{color:'var(--pencil)'}}>◇</span>
          <b>parcels_2024</b>
          <span style={{color:'#bbb'}}>→</span>
          <span className="tag">FeatureServer</span>
          <span className="mono">public / public-works-fs / layer 8</span>
          <span style={{color:'#bbb'}}>·</span>
          <span>slot label "Parcels"</span>
          <div style={{flex:1}} />
          <Badge kind="ok">compatible</Badge>
          <Badge>draft</Badge>
        </div>

        <div className="body" style={{display:'grid',gridTemplateColumns:'1.4fr 360px',gap:16, padding:'14px 18px'}}>
          <div className="col" style={{gap:10}}>
            <div>
              <div className="row" style={{marginBottom:6}}>
                <h2 style={{margin:0,font:'600 16px var(--ui)'}}>Preview how this layer will look</h2>
                <div style={{flex:1}} />
                <div style={{display:'inline-flex', border:'1px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:10.5}}>
                  <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Just this layer</div>
                  <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Whole service</div>
                  <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Layer JSON</div>
                  <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Metadata</div>
                  <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>Sample features</div>
                </div>
              </div>

              <MapPreview mode="layer" height={420} />

              {/* under-map controls */}
              <div className="row" style={{marginTop:8, gap:8, flexWrap:'wrap'}}>
                <span className="muted" style={{fontSize:11}}>Preview controls</span>
                <div style={{flex:1}} />
                <div className="row" style={{gap:4, fontSize:10.5}}>
                  <span className="muted">scale</span>
                  <span className="mono">1:500</span>
                  <input type="range" defaultValue={60} style={{width:160}} />
                  <span className="mono">1:15M</span>
                </div>
                <FiltChip on>labels</FiltChip>
                <FiltChip on>popups</FiltChip>
                <FiltChip>highlight</FiltChip>
                <FiltChip>basemap: light</FiltChip>
              </div>
            </div>

            {/* Style legend strip */}
            <div className="card" style={{padding:'8px 12px'}}>
              <div className="row" style={{marginBottom:6}}>
                <h3 style={{margin:0}}>Style legend</h3>
                <span className="muted" style={{fontSize:11, marginLeft:8}}>inherited from resource · class breaks on <span className="mono">area_m2</span></span>
                <div style={{flex:1}} />
                <Btn ghost sm>Override style for this slot…</Btn>
              </div>
              <div className="row" style={{gap:10, flexWrap:'wrap', fontSize:10.5}}>
                {[
                  ['#f7f4e8','0 – 740 m²'],
                  ['#ead78a','740 – 1,420'],
                  ['#d9a23a','1,420 – 2,140'],
                  ['#b56b1c','2,140 – 3,920'],
                  ['#612d0a','3,920+'],
                ].map(([c,l]) => (
                  <div key={l} className="row" style={{gap:4}}>
                    <span style={{width:14,height:14,background:c,border:'1px solid #999',display:'inline-block'}} />
                    <span>{l}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* RIGHT COL */}
          <div className="col">
            <Callout kind="info">
              <b>What this is.</b> A live render of the layer Honua will publish at this slot. Style and labels come from the canonical resource; popup fields and aliases come from this slot's overrides.
            </Callout>

            <div className="card">
              <h3>This slot</h3>
              <dl className="kv">
                <dt>Object ID</dt><dd className="mono">gid</dd>
                <dt>Display field</dt><dd className="mono">parcel_id</dd>
                <dt>Popup fields</dt><dd>4 <span className="muted">(of 23 exposed)</span></dd>
                <dt>Owner redacted</dt><dd><Badge kind="warn">yes</Badge></dd>
                <dt>Labels at</dt><dd className="mono">1:500 – 1:25k</dd>
                <dt>Cache TTL</dt><dd>30 min</dd>
              </dl>
              <div className="row">
                <Btn sm>← Edit slot</Btn>
                <Btn sm>Edit popup fields</Btn>
              </div>
            </div>

            <div className="card">
              <h3>Compare against</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                {[
                  ['◇ Resource canonical','same style, all 24 fields'],
                  ['public-works-ms / layer 2','MapServer · rendered tiles'],
                  ['features-public / parcels_2024','OGC API · GeoJSON'],
                  ['stac-public','collection metadata only'],
                ].map(([k,v]) => (
                  <div key={k} className="row" style={{padding:'3px 0', borderBottom:'1px dashed #eee'}}>
                    <span style={{flex:1}}>{k}</span>
                    <span className="muted mono" style={{fontSize:10}}>{v}</span>
                  </div>
                ))}
              </div>
              <Btn ghost sm>Side-by-side compare…</Btn>
            </div>

            <Ann>map uses 1k sampled features. published service serves all 1.28M.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Fields</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn ghost>Skip to Review →</Btn>
            <Btn kind="p">Continue · Access →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function WizPublishTarget() {
  // Step 1: pick folder / service / kind of slot. Resource-initiated entry.
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Data resources','parcels_2024','Publish']} />
      <div className="wiz">
        <Stepper steps={['Target','Compatibility','Slot','Fields','Projection','Access','Review']} on={0} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{color:'var(--pencil)'}}>◇</span>
          <b>parcels_2024</b>
          <span className="muted">· MultiPolygon · 4326 · 1.28M features · 24 fields</span>
          <div style={{flex:1}} />
          <span className="muted" style={{fontSize:11}}>already published to 5 slots</span>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24}}>
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Where should this resource appear?</h2>
            <div className="muted" style={{marginBottom:14, fontSize:11.5}}>
              Pick a folder + service to drop a new layer slot into. We'll tell you in step 2 what's compatible.
            </div>

              {/* segmented mode at the top of step 1 */}
              <div style={{
                display:'inline-flex', border:'1.2px solid var(--ink)', borderRadius:5,
                overflow:'hidden', fontSize:11, marginBottom:12
              }}>
                <div style={{padding:'6px 14px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Use existing service</div>
                <div style={{padding:'6px 14px', background:'#fff', color:'#666'}}>Create new service</div>
              </div>

            <div className="row" style={{marginBottom:10}}>
              <FiltChip on x>only compatible</FiltChip>
              <FiltChip>only services I own</FiltChip>
              <input className="inp" style={{width:220, height:22, marginLeft:'auto'}} placeholder="Filter…" readOnly />
            </div>

            {/* Tree picker */}
            <div style={{border:'1px solid #e4e4e4', borderRadius:6, overflow:'hidden', background:'#fff'}}>
              {[
                { d:0, ico:'🗀', name:'public', meta:'5 services', open:true },
                  { d:1, ico:'◈', name:'public-works-fs', kind:'FeatureServer', meta:'8 layers · live · public read',
                    note:'already publishes parcels_2024 at layer 0 — adding another would create a 2nd slot', on:true },
                  { d:1, ico:'◈', name:'public-works-ms', kind:'MapServer', meta:'8 layers · live',
                    note:'already publishes parcels_2024 at layer 2' },
                  { d:1, ico:'◈', name:'features-public', kind:'OGC API Features', meta:'38 collections · live',
                    note:'already publishes parcels_2024 as a collection' },
                  { d:1, ico:'◈', name:'tiles-public', kind:'WMTS', meta:'21 layers · live',
                    note:'already publishes parcels_2024 (stale)' },
                  { d:1, ico:'◈', name:'stac-public', kind:'STAC', meta:'42 collections · live',
                    note:'already publishes parcels_2024 metadata' },
                { d:0, ico:'🗀', name:'catalogs', meta:'2 services', open:true },
                  { d:1, ico:'◈', name:'dcat-eu', kind:'DCAT', meta:'54 datasets · live',
                    note:'already publishes parcels_2024 metadata' },
                  { d:1, ico:'◈', name:'records-public', kind:'OGC Records', meta:'38 records · live',
                    note:'compatible · would add a new record', fresh:true },
                { d:0, ico:'🗀', name:'internal', meta:'3 services · auth required', open:true },
                  { d:1, ico:'◈', name:'fs-internal', kind:'FeatureServer', meta:'24 layers · live',
                    note:'compatible · would add a new layer slot', fresh:true },
                  { d:1, ico:'◈', name:'ms-internal', kind:'MapServer', meta:'12 layers · live',
                    note:'compatible · would add a new layer slot', fresh:true },
                  { d:1, ico:'◈', name:'odata-bi', kind:'OData', meta:'8 entity sets · degraded',
                    note:'compatible · MultiPolygon will be sent as WKB column', fresh:true, warn:true },
                { d:0, ico:'🗀', name:'imagery', meta:'1 service · raster',
                  note:'not compatible — this resource is vector', incompat:true },
              ].map((n, i) => (
                <div key={i} style={{
                  paddingLeft: 10 + n.d * 14, paddingRight: 12,
                  display:'flex', alignItems:'center', gap:8, height:34,
                  borderBottom:'1px solid #f1f1f1',
                  background: n.on ? '#fffae0' : 'transparent',
                  borderLeft: n.on ? '3px solid var(--ink)' : '3px solid transparent',
                  opacity: n.incompat ? 0.55 : 1, cursor:'pointer'
                }}>
                  <input type="radio" readOnly defaultChecked={n.on} disabled={n.incompat} />
                  <span style={{width:14, textAlign:'center', color:'#666'}}>{n.ico}</span>
                  <div style={{flex:1, minWidth:0}}>
                    <div style={{fontSize:11.5, fontWeight: n.kind ? 600 : 500}}>
                      {n.name}
                      {n.kind && <span className="tag" style={{marginLeft:6}}>{n.kind}</span>}
                    </div>
                    <div className="muted" style={{fontSize:10.5, overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap'}}>
                      {n.meta} {n.note && <span style={{color: n.fresh ? 'var(--ok)' : n.incompat ? 'var(--bad)' : n.warn ? 'var(--warn)' : '#aaa'}}>· {n.note}</span>}
                    </div>
                  </div>
                  {n.fresh && <Badge kind="ok">new slot</Badge>}
                  {n.incompat && <Badge kind="bad">incompat.</Badge>}
                </div>
              ))}
            </div>

            <div className="row" style={{marginTop:10}}>
              <Btn ghost sm>+ New folder</Btn>
              <Btn ghost sm>+ New service…</Btn>
            </div>
          </div>

          <div className="col">
            <Callout kind="info">
              <b>One resource → many slots.</b> A resource can sit in many services at once. Each slot can override layer name / aliases / field exposure, but never the meaning.
            </Callout>

            <div className="card">
              <h3>Selected</h3>
              <dl className="kv">
                <dt>Folder</dt><dd className="mono">public</dd>
                <dt>Service</dt><dd className="mono">public-works-fs</dd>
                <dt>Service kind</dt><dd>FeatureServer</dd>
                <dt>This would be</dt><dd className="mono">layer 8 · new slot</dd>
                <dt>Default slot label</dt><dd>Parcels</dd>
              </dl>
            </div>

            <div className="card">
              <h3>Already published to</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                {[
                  'public-works-fs / layer 0',
                  'public-works-ms / layer 2',
                  'features-public / parcels_2024',
                  'stac-public / parcels_2024',
                  'dcat-eu / parcels_2024',
                ].map(t => (
                  <div key={t} className="row" style={{padding:'2px 0', borderBottom:'1px dashed #eee'}}>
                    <Badge kind="ok">live</Badge>
                    <span className="mono" style={{fontSize:10.5, marginLeft:6}}>{t}</span>
                  </div>
                ))}
              </div>
            </div>

            <Ann red>publish creates a NEW Publication record. existing slots are untouched.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>Cancel</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Compatibility →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function WizPublishInlineService() {
  // Step 1 of the Publish wizard, with the inline "+ New service" drawer open.
  // No context switch — service gets created inside the wizard and selected automatically.
  return (
    <div className="scr scr--noside" style={{position:'relative'}}>
      <TopBar crumbs={['Data resources','parcels_2024','Publish']} />
      <div className="wiz">
        <Stepper steps={['Target','Compatibility','Slot','Fields','Projection','Access','Review']} on={0} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{color:'var(--pencil)'}}>◇</span>
          <b>parcels_2024</b>
          <span className="muted">· MultiPolygon · 4326 · 1.28M features</span>
          <div style={{flex:1}}/>
          <span className="muted" style={{fontSize:11}}>creating a new service inline</span>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, opacity:0.55, pointerEvents:'none'}}>
          {/* dimmed background context (the picker behind the drawer) */}
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Where should this resource appear?</h2>
            <div className="muted" style={{marginBottom:14, fontSize:11.5}}>
              Pick a folder + service to drop a new layer slot into. Don't see one that fits? Create a new service inline — you won't leave this flow.
            </div>
            <div style={{border:'1px solid #e4e4e4', borderRadius:6, overflow:'hidden', background:'#fff'}}>
              {[
                ['🗀','public','5 services',0,true],
                ['◈','public-works-fs','FeatureServer · 8 layers',1,false],
                ['◈','features-public','OGC API Features · 38 collections',1,false],
                ['◈','stac-public','STAC · 42 collections',1,false],
                ['🗀','catalogs','2 services',0,true],
                ['◈','dcat-eu','DCAT · 54 datasets',1,false],
                ['🗀','internal','3 services · auth required',0,true],
              ].map(([ico,name,meta,d],i) => (
                <div key={i} style={{paddingLeft:10 + d*14, paddingRight:12, height:30, display:'flex', alignItems:'center', gap:8, borderBottom:'1px solid #f1f1f1', fontSize:11.5}}>
                  <span style={{width:14, textAlign:'center', color:'#666'}}>{ico}</span>
                  <span style={{flex:1}}>{name}</span>
                  <span className="muted" style={{fontSize:10}}>{meta}</span>
                </div>
              ))}
            </div>
          </div>
          <div className="col">
            <div className="card"><h3>Selected</h3><div className="muted" style={{fontSize:11}}>none yet</div></div>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>Cancel</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Compatibility →</Btn>
          </div>
        </div>
      </div>

      {/* Dim layer */}
      <div style={{
        position:'absolute', top:38, left:0, right:0, bottom:0,
        background:'rgba(20,20,20,0.18)', pointerEvents:'none'
      }} />

      {/* Inline drawer — sized smaller than full drawer, anchored bottom-right of the wizard */}
      <div style={{
        position:'absolute', top: 80, right: 24, bottom: 80,
        width: 540, background:'#fff', border:'2px solid var(--ink)', borderRadius:8,
        boxShadow:'0 24px 60px rgba(0,0,0,.18)',
        display:'flex', flexDirection:'column'
      }}>
        <div style={{padding:'12px 14px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center', gap:8, background:'var(--accent)'}}>
          <span style={{fontSize:14}}>＋</span>
          <b style={{fontSize:13}}>Create service inline</b>
          <span className="muted" style={{fontSize:10.5}}>then continue publishing parcels_2024 into it</span>
          <div style={{flex:1}}/>
          <span style={{cursor:'pointer'}}>×</span>
        </div>
        <div style={{padding:14, overflow:'auto', flex:1}}>
          <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
            <Field label="Folder" hint="organisational only · no security implications">
              <Sel value="public" />
            </Field>
            <Field label="Service kind">
              <Sel value="FeatureServer (GeoServices)" />
            </Field>
            <Field label="Name" hint="lowercase, dashes ok">
              <Inp mono value="parcels-public-fs" />
            </Field>
            <Field label="Route">
              <Inp mono value="/public/parcels-fs/FeatureServer" />
            </Field>
            <Field label="Anonymous access?">
              <Sel value="Yes — read only" />
            </Field>
            <Field label="Default cache TTL">
              <Sel value="30 min" />
            </Field>
            <Field label="Output formats">
              <Inp value="JSON, GeoJSON, PBF" />
            </Field>
            <Field label="Max record count">
              <Inp mono value="5,000" />
            </Field>
          </div>

          <Callout kind="info" style={{marginTop:6}}>
            <b>What happens.</b> Honua creates the empty service, drops you back into step 1 with it pre-selected, then continues into Compatibility → Slot → Projection. Total round-trip: ~10 seconds of clicks. You can edit anything later in <span className="mono">Services & catalogs → parcels-public-fs</span>.
          </Callout>

          <div className="card" style={{marginTop:10}}>
            <h3>What you'll end up with</h3>
            <div className="col" style={{gap:4, fontSize:11}}>
              <div className="row"><span className="muted" style={{flex:1}}>Service</span><span className="mono">parcels-public-fs (new)</span></div>
              <div className="row"><span className="muted" style={{flex:1}}>First layer slot</span><span className="mono">layer 0 · Parcels → ◇ parcels_2024</span></div>
              <div className="row"><span className="muted" style={{flex:1}}>Public URL</span><span className="mono" style={{fontSize:10}}>/public/parcels-fs/FeatureServer/0</span></div>
            </div>
          </div>
        </div>
        <div style={{padding:'10px 14px', borderTop:'1px solid #e4e4e4', display:'flex', gap:8}}>
          <Btn ghost sm>Cancel</Btn>
          <div style={{flex:1}}/>
          <Btn sm>Save service draft &amp; continue</Btn>
          <Btn kind="p" sm>Create &amp; continue →</Btn>
        </div>
      </div>
    </div>
  );
}

function AddLayerToService() {
  // Inverse direction: from a Service, "+ Add layer" lets you pick a Data Resource.
  // Service-led entry — admin who builds an endpoint first, then publishes into it.
  return (
    <div className="scr scr--noside" style={{position:'relative'}}>
      <TopBar crumbs={['Services & catalogs','public-works-fs','+ Add layer']} />
      <div className="wiz">
        <Stepper steps={['Resource','Compatibility','Slot','Fields','Projection','Access','Review']} on={0} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{color:'#666'}}>◈</span>
          <b>public-works-fs</b>
          <span className="tag">FeatureServer</span>
          <span className="muted">· 8 layers · public · /public/pw/FeatureServer</span>
          <div style={{flex:1}}/>
          <span className="muted" style={{fontSize:11}}>this layer will become slot <span className="mono">/FeatureServer/8</span></span>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24}}>
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Pick a Data Resource to publish here</h2>
            <div className="muted" style={{marginBottom:14, fontSize:11.5}}>
              FeatureServer carries vector features with attributes. Honua will warn you if a resource isn't compatible.
            </div>

            <div className="row" style={{marginBottom:10}}>
              <FiltChip on x>only compatible (Polygon · Point · Line)</FiltChip>
              <FiltChip>not yet in this service</FiltChip>
              <FiltChip>my resources</FiltChip>
              <input className="inp" style={{width:200, height:22, marginLeft:'auto'}} placeholder="Filter 128 resources…" readOnly />
            </div>

            <div style={{border:'1px solid #e4e4e4', borderRadius:6, overflow:'hidden'}}>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th style={{width:24}}></th>
                  <th>Resource</th><th>Type</th><th>Features</th><th>Status</th><th>Note</th>
                </tr></thead>
                <tbody>
                  <tr><td><input type="radio" readOnly /></td><td><span style={{color:'var(--pencil)'}}>◇</span> <b>parcels_2024</b></td><td>Polygon</td><td className="num mono">1.28M</td><td><Badge kind="ok">Published</Badge></td><td className="muted">already at layer 0 — adding would create a 2nd slot</td></tr>
                  <tr className="sel"><td><input type="radio" readOnly defaultChecked /></td><td><span style={{color:'var(--pencil)'}}>◇</span> <b>hydrants_2024</b></td><td>Point</td><td className="num mono">14k</td><td><Badge kind="draft">Draft</Badge></td><td style={{color:'var(--ok)'}}>compatible · would become new layer slot</td></tr>
                  <tr><td><input type="radio" readOnly /></td><td><span style={{color:'var(--pencil)'}}>◇</span> <b>monitoring_sites</b></td><td>Point</td><td className="num mono">4.2k</td><td><Badge kind="ok">Published</Badge></td><td style={{color:'var(--ok)'}}>compatible · not in this service yet</td></tr>
                  <tr><td><input type="radio" readOnly /></td><td><span style={{color:'var(--pencil)'}}>◇</span> <b>roads_osm</b></td><td>Line</td><td className="num mono">482k</td><td><Badge kind="ok">Published</Badge></td><td className="muted">already at layer 1</td></tr>
                  <tr><td><input type="radio" readOnly /></td><td><span style={{color:'var(--pencil)'}}>◇</span> <b>fire_observations</b></td><td>Point</td><td className="num mono">2.1M</td><td><Badge kind="draft">Draft</Badge></td><td style={{color:'var(--ok)'}}>compatible · not in this service yet</td></tr>
                  <tr style={{opacity:0.55}}><td><input type="radio" readOnly disabled /></td><td><span style={{color:'#aaa'}}>◇</span> land_cover_2024</td><td>Raster</td><td className="num mono">—</td><td><Badge>—</Badge></td><td style={{color:'var(--bad)'}}>not compatible · raster → use ImageServer or WMTS</td></tr>
                  <tr style={{opacity:0.55}}><td><input type="radio" readOnly disabled /></td><td><span style={{color:'#aaa'}}>◇</span> air_quality_obs</td><td>Point</td><td className="num mono">14.8M</td><td><Badge kind="ok">Published</Badge></td><td className="muted">already at layer 6</td></tr>
                </tbody>
              </table>
            </div>

            <div className="row" style={{marginTop:8, fontSize:11}}>
              <span className="muted">Don't see it?</span>
              <Btn ghost sm>+ Create resource from connection</Btn>
              <Btn ghost sm>+ Import file</Btn>
              <Btn ghost sm>+ Import remote service</Btn>
              <Ann style={{marginLeft:'auto'}}>resource-creation entries fold into this flow too — no dead-ends.</Ann>
            </div>
          </div>

          <div className="col">
            <Callout kind="info">
              <b>Service-led entry.</b> You started from a service, so picking the resource comes first. The remaining 6 steps are identical to the resource-led Publish wizard — Honua keeps the same model on both paths.
            </Callout>

            <div className="card">
              <h3>Selected</h3>
              <dl className="kv">
                <dt>Resource</dt><dd><span style={{color:'var(--pencil)'}}>◇</span> hydrants_2024</dd>
                <dt>Type</dt><dd>Point · 4326</dd>
                <dt>Features</dt><dd className="mono">14,028</dd>
                <dt>Will become</dt><dd className="mono">layer 8 · "Hydrants"</dd>
                <dt>Default access</dt><dd>inherits resource · public read</dd>
              </dl>
            </div>

            <div className="card">
              <h3>Both directions arrive here</h3>
              <div style={{fontSize:11.5, lineHeight:1.55}}>
                Whether you started from <b>a resource</b> ("publish this to…") or <b>a service</b> ("add a layer here…"), you converge on the same 7-step wizard. The same Publication record is created either way.
              </div>
            </div>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>Cancel</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Compatibility →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function WizPublishTargetNew() {
  // Step 1 — same wizard, but "create a new service" mode instead of picking existing.
  // Shows: a segmented mode toggle at top, then a compact form so a new service can
  // be born from inside the publish flow (and parcels_2024 lands as its first slot).
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Data resources','parcels_2024','Publish']} />
      <div className="wiz">
        <Stepper steps={['Target','Compatibility','Slot','Fields','Projection','Access','Review']} on={0} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{color:'var(--pencil)'}}>◇</span>
          <b>parcels_2024</b>
          <span className="muted">· MultiPolygon · 4326 · 1.28M features · 24 fields</span>
          <div style={{flex:1}} />
          <span className="muted" style={{fontSize:11}}>already published to 5 slots</span>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24}}>
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Where should this resource appear?</h2>
            <div className="muted" style={{marginBottom:14, fontSize:11.5}}>
              You can drop it into an existing service, or stand up a new service right here without leaving this flow.
            </div>

            {/* segmented mode */}
            <div style={{
              display:'inline-flex', border:'1.2px solid var(--ink)', borderRadius:5,
              overflow:'hidden', fontSize:11, marginBottom:14
            }}>
              <div style={{padding:'6px 14px', background:'#fff', color:'#666', borderRight:'1px solid var(--ink)'}}>Use existing service</div>
              <div style={{padding:'6px 14px', background:'var(--accent)', fontWeight:600}}>Create new service</div>
            </div>

            <div className="card" style={{borderColor:'var(--ink)'}}>
              <h3>New service · runtime essentials</h3>
              <div className="muted" style={{fontSize:11}}>
                Minimum needed to start publishing. Full runtime settings (cache rules, output formats, max records, etc.) can be tuned later in <span className="mono">Services → settings</span>.
              </div>

              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
                <Field label="Service kind" hint="determines available layer-slot semantics">
                  <Sel value="GeoServices FeatureServer" />
                </Field>
                <Field label="Folder"><Sel value="public" /></Field>
                <Field label="Service name" hint="lower-case, dashes ok · must be unique in folder"><Inp mono value="parcels-fs" /></Field>
                <Field label="Route" hint="derived from kind + folder + name. Override only in advanced.">
                  <div style={{
                    height:26, border:'1px dashed #c0c0c0', borderRadius:4, background:'#fafafa',
                    padding:'0 8px', display:'flex', alignItems:'center', gap:2,
                    font:'11px var(--mono)', color:'#666',
                  }}>
                    <span style={{color:'#aaa'}}>/</span>
                    <span style={{color:'var(--pencil)'}}>public</span>
                    <span style={{color:'#aaa'}}>/</span>
                    <span style={{color:'var(--pencil)'}}>parcels-fs</span>
                    <span style={{color:'#aaa'}}>/</span>
                    <span style={{color:'var(--pencil)'}}>FeatureServer</span>
                    <span style={{flex:1}} />
                    <span style={{color:'#bbb',fontSize:9.5}}>auto</span>
                  </div>
                </Field>
                <Field label="Anonymous access"><Sel value="Public read" /></Field>
                <Field label="Default cache TTL"><Sel value="30 min" /></Field>
              </div>

              <div className="callout callout--info" style={{marginTop:4}}>
                <b>parcels_2024 will land as <span className="mono">layer 0</span></b> in this new service. You can add more layer slots later from other resources, or leave it as a single-resource service.
              </div>

              <div className="row" style={{marginTop:4}}>
                <Btn ghost sm>Advanced runtime settings…</Btn>
                <div style={{flex:1}} />
                <span className="muted" style={{fontSize:10.5}}>Created when you reach <b>Review</b>. Until then, this is a draft.</span>
              </div>
            </div>

            <div style={{marginTop:14}}>
              <div className="muted" style={{fontSize:10.5, marginBottom:6, textTransform:'uppercase', letterSpacing:'0.06em'}}>or — start a different kind</div>
              <div className="row" style={{gap:6, flexWrap:'wrap'}}>
                {[
                  ['GeoServices FeatureServer','vector layers'],
                  ['GeoServices MapServer','rendered map layers'],
                  ['GeoServices ImageServer','rasters'],
                  ['OGC API Features','feature collections'],
                  ['WMS','rendered map service'],
                  ['WMTS','tiled map'],
                  ['STAC','catalog'],
                  ['DCAT','catalog'],
                  ['OGC Records','catalog'],
                  ['OData','tabular'],
                ].map(([k,desc],i) => (
                  <div key={i} style={{
                    border:'1px solid #d0d0d0', borderRadius:5, padding:'6px 10px',
                    fontSize:11, background:'#fff', cursor:'pointer',
                    minWidth:160,
                  }}>
                    <div style={{fontWeight:600}}>{k}</div>
                    <div className="muted" style={{fontSize:10}}>{desc}</div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="col">
            <Callout kind="info">
              <b>Why this works.</b> A service is just a runtime endpoint + a set of layer slots. The publish flow lets you stand one up on the way to the first publication so you don't have to break stride.
            </Callout>

            <div className="card">
              <h3>What will happen on Review</h3>
              <ol style={{margin:'0 0 0 16px', padding:0, fontSize:11.5, lineHeight:1.65}}>
                <li>Honua creates service <span className="mono">public-works-fs</span> as a FeatureServer, public read.</li>
                <li>parcels_2024 lands as <span className="mono">layer 0</span> (a Publication record, not a copy).</li>
                <li>The new service appears under <span className="mono">Services → public</span>.</li>
                <li>Anyone with public-read can hit <span className="mono">/public/parcels-fs/FeatureServer/0</span>.</li>
              </ol>
            </div>

            <Ann red>
              creating service ≠ publishing.<br/>
              the service is empty &amp; idle until the wizard finishes Review.
            </Ann>

            <Ann>
              flip back to "use existing" anytime — your draft service settings stick around.
            </Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>Cancel</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Compatibility →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { PublishFlowMap, WizPublish, WizPublishTarget, WizPublishInlineService, AddLayerToService });
