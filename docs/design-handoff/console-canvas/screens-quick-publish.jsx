// One-shot "Publish service" — common-case flow.
// 3 steps cleanly separating service-level vs layer-level settings:
//   Service → Layer → Review
// Step 1 = service settings (CRS, capabilities, cache, output formats). Pick existing or create new.
// Step 2 = layer settings: bind an existing Data Resource OR create a new one.
//          The resource is the canonical home; many layers can reuse the same resource.
// Step 3 = review what gets created (resource? slot. auto-catalog).

function ModeBar({ on }) {
  return (
    <div style={{padding:'8px 18px', borderBottom:'1px solid #e4e4e4', background:'#fafafa', display:'flex',alignItems:'center',gap:8,fontSize:11}}>
      <span className="muted" style={{textTransform:'uppercase',letterSpacing:'0.06em',fontSize:10}}>Mode</span>
      <div style={{display:'inline-flex', border:'1.2px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:11}}>
        <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Quick publish</div>
        <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>Author resource first (advanced)</div>
      </div>
      <span className="muted" style={{marginLeft:8}}>One-shot. Honua creates the underlying Data Resource implicitly.</span>
      <div style={{flex:1}}/>
      <span className="hand" style={{font:'13px var(--hand)', color:'var(--pencil)'}}>3 steps · ~30 seconds</span>
    </div>
  );
}

function CtxSource() {
  return (
    <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
      <span className="tag">Source</span>
      <b>prod-postgis</b>
      <span className="mono">public.parcels_2024</span>
      <span className="muted">· MultiPolygon · 4326 · 1.28M rows · PK gid</span>
      <div style={{flex:1}}/>
      <a style={{fontSize:10.5,color:'var(--pencil)',cursor:'pointer'}}>← change source</a>
    </div>
  );
}

/* ---------- STEP 2A: PICK / CREATE SERVICE ---------- */
function WizQuickPublishService() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Publish service']} />
      <div className="wiz">
        <Stepper steps={['Service','Layer','Review']} on={0} />
        <ModeBar />

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24}}>
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>What service should this layer live in?</h2>
            <div className="muted" style={{marginBottom:14, fontSize:11.5}}>
              A service is the container. <b>Service-level</b> settings (CRS, capabilities, cache) live here. The layer-specific stuff (name, fields, access) is the next step.
            </div>

            {/* mode picker */}
            <div style={{display:'inline-flex', border:'1.2px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:11, marginBottom:12}}>
              <div style={{padding:'6px 14px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Use existing service</div>
              <div style={{padding:'6px 14px', background:'#fff', color:'#666'}}>Create new service</div>
            </div>

            {/* Tree of existing services, compatible filter on */}
            <div className="row" style={{marginBottom:6, fontSize:11}}>
              <FiltChip on x>kind: FeatureServer</FiltChip>
              <FiltChip on x>compatible (vector polygon)</FiltChip>
              <FiltChip>my folders</FiltChip>
              <input className="inp" style={{width:200, height:22, marginLeft:'auto'}} placeholder="Filter…" readOnly />
            </div>

            <div style={{border:'1px solid #e4e4e4', borderRadius:6, overflow:'hidden', background:'#fff'}}>
              {[
                { d:0, ico:'🗀', name:'public', meta:'5 services', open:true },
                  { d:1, ico:'◈', name:'public-works-fs', meta:'8 layers · live · public read · CRS 4326', on:true,
                    slot:'next slot: layer 8' },
                  { d:1, ico:'◈', name:'features-public', meta:'OGC API — wrong kind for FeatureServer', inert:true },
                { d:0, ico:'🗀', name:'internal', meta:'3 services · auth required', open:true },
                  { d:1, ico:'◈', name:'fs-internal', meta:'24 layers · live · CRS 4326',
                    slot:'next slot: layer 24' },
                { d:0, ico:'🗀', name:'imagery', meta:'no FeatureServers here', inert:true },
              ].map((n, i) => (
                <div key={i} style={{
                  paddingLeft: 10 + n.d * 14, paddingRight: 12,
                  display:'flex', alignItems:'center', gap:8, height:34,
                  borderBottom:'1px solid #f1f1f1',
                  background: n.on ? '#fffae0' : 'transparent',
                  borderLeft: n.on ? '3px solid var(--ink)' : '3px solid transparent',
                  opacity: n.inert ? 0.5 : 1, cursor: n.inert ? 'default' : 'pointer'
                }}>
                  {n.d === 1 && !n.inert && <input type="radio" readOnly defaultChecked={n.on} />}
                  {(n.d === 0 || n.inert) && <span style={{width:13}} />}
                  <span style={{width:14, textAlign:'center', color:'#666'}}>{n.ico}</span>
                  <div style={{flex:1, minWidth:0}}>
                    <div style={{fontSize:11.5, fontWeight: n.d === 1 ? 600 : 500}}>{n.name}</div>
                    <div className="muted" style={{fontSize:10.5, overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap'}}>
                      {n.meta}{n.slot && <span style={{color:'var(--ok)'}}> · {n.slot}</span>}
                    </div>
                  </div>
                </div>
              ))}
            </div>

            {/* inline new-service affordance */}
            <div style={{border:'1px dashed #c5c5c5', borderRadius:6, padding:'10px 12px', background:'#fafafa', marginTop:10}}>
              <div className="row" style={{marginBottom:4}}>
                <b style={{fontSize:11.5}}>Don't see what you want? Create a new service →</b>
                <div style={{flex:1}}/>
                <Btn ghost sm>Create new service…</Btn>
              </div>
              <div className="muted" style={{fontSize:10.5}}>
                New services have their own settings step — CRS, capabilities, max record count, cache, output formats — before you get to the layer.
              </div>
            </div>
          </div>

          <div className="col">
            <Callout kind="info">
              <b>Why this is one step.</b> When you reuse an existing service, you accept its settings (CRS, capabilities, cache, etc.) as-is. They're shared by every layer in that service. To use different service-level settings, create a new one.
            </Callout>

            <div className="card">
              <h3>You're publishing into</h3>
              <dl className="kv">
                <dt>Service</dt><dd className="mono">public-works-fs</dd>
                <dt>Kind</dt><dd>FeatureServer</dd>
                <dt>Folder</dt><dd className="mono">public</dd>
                <dt>Default CRS</dt><dd className="mono">EPSG:4326</dd>
                <dt>Anonymous</dt><dd>yes (folder default)</dd>
                <dt>Cache TTL</dt><dd>30 min</dd>
                <dt>Capabilities</dt><dd>Query, Extract</dd>
              </dl>
              <Btn ghost sm>View service settings ↗</Btn>
            </div>

            <Ann>service settings are SHARED across all layers in the service. layer settings (next step) are PER-layer.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>Cancel</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Layer →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- STEP 2B: CREATE NEW SERVICE (alt of 2A) ---------- */
function WizQuickPublishNewService() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Publish service']} />
      <div className="wiz">
        <Stepper steps={['Service','Layer','Review']} on={0} />
        <ModeBar />

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, overflow:'auto'}}>
          <div>
            <div className="row" style={{marginBottom:6}}>
              <h2 style={{margin:0,font:'600 16px var(--ui)'}}>Create a new service</h2>
              <div style={{flex:1}}/>
              <div style={{display:'inline-flex', border:'1.2px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:11}}>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Use existing</div>
                <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600}}>Create new</div>
              </div>
            </div>
            <div className="muted" style={{marginBottom:14, fontSize:11.5}}>
              These are service-level settings — shared across every layer added to this service later.
            </div>

            {/* SERVICE IDENTITY */}
            <div className="card" style={{marginBottom:10}}>
              <h3>Identity</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr 1fr', gap:10}}>
                <Field label="Service kind" hint="determines layer-slot semantics"><Sel value="GeoServices FeatureServer" /></Field>
                <Field label="Folder"><Sel value="public" /></Field>
                <Field label="Service name" hint="lowercase, dashes"><Inp mono value="parcels-fs" /></Field>
              </div>
              <Field label="Display title"><Inp value="Parcels (public)" /></Field>
              <Field label="Route" hint="derived from folder + name + kind">
                <div style={{height:26, border:'1px dashed #c0c0c0', borderRadius:4, background:'#fafafa', padding:'0 8px', display:'flex', alignItems:'center', font:'11px var(--mono)', color:'#666'}}>
                  /public/parcels-fs/FeatureServer
                  <span style={{flex:1}}/><span style={{color:'#bbb',fontSize:9.5}}>auto</span>
                </div>
              </Field>
            </div>

            {/* SPATIAL REFERENCE */}
            <div className="card" style={{marginBottom:10}}>
              <h3>Spatial reference</h3>
              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
                <Field label="Default CRS" hint="what the service reports as native"><Sel value="EPSG:4326 · WGS 84 (suggested from source)" /></Field>
                <Field label="Z-aware"><Sel value="no" /></Field>
              </div>
              <Field label="Allow consumer-requested CRSs" hint="re-projected on the fly">
                <div className="row" style={{flexWrap:'wrap', gap:6, fontSize:11}}>
                  <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> EPSG:3857</label>
                  <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> EPSG:4269</label>
                  <label className="row" style={{gap:4}}><input type="checkbox" readOnly /> EPSG:2227</label>
                  <label className="row" style={{gap:4}}><input type="checkbox" readOnly /> any</label>
                </div>
              </Field>
            </div>

            {/* CAPABILITIES */}
            <div className="card" style={{marginBottom:10}}>
              <h3>Capabilities</h3>
              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10, fontSize:11.5}}>
                <div>
                  <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Read</div>
                  <div className="col" style={{gap:3}}>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> Query</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> Extract (bulk)</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> GetFeatureInfo</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> Statistics</label>
                  </div>
                </div>
                <div>
                  <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Write</div>
                  <div className="col" style={{gap:3}}>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly /> Create</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly /> Update</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly /> Delete</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly /> Sync / replicas</label>
                  </div>
                </div>
              </div>
            </div>

            {/* QUERY LIMITS & FORMATS */}
            <div className="card" style={{marginBottom:10}}>
              <h3>Limits &amp; formats</h3>
              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
                <Field label="Default max record count"><Inp mono value="5,000" /></Field>
                <Field label="Cache TTL"><Sel value="30 min" /></Field>
                <Field label="Output formats">
                  <div className="row" style={{flexWrap:'wrap', gap:4, fontSize:11}}>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> JSON</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> GeoJSON</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly defaultChecked /> PBF</label>
                    <label className="row" style={{gap:4}}><input type="checkbox" readOnly /> HTML</label>
                  </div>
                </Field>
                <Field label="Anonymous access"><Sel value="Public read (folder default)" /></Field>
              </div>
            </div>

            {/* CATALOG REGISTRATION */}
            <div className="card" style={{marginBottom:10}}>
              <h3>Register in catalog</h3>
              <div className="muted" style={{fontSize:11, marginBottom:6}}>
                Add a discoverable entry that points back at this service. Off = service exists but is hidden from catalog search. The endpoint itself must be enabled server-wide (see <span className="mono">Settings → Catalog endpoints</span>).
              </div>
              <label className="row" style={{gap:8, padding:'6px 8px', border:'1.2px solid var(--ink)', borderRadius:5, background:'#fffae0', cursor:'pointer'}}>
                <input type="checkbox" readOnly defaultChecked />
                <div style={{flex:1}}>
                  <div style={{fontSize:11.5, fontWeight:600}}>Register in Esri catalog</div>
                  <div className="muted" style={{fontSize:10.5}}>recommended for FeatureServer · keeps title, description, thumbnail in sync · server-wide endpoint is <Badge kind="ok">ON</Badge></div>
                </div>
                <Badge kind="info">default on</Badge>
              </label>
            </div>

            <Callout kind="info">
              <b>Service vs layer.</b> Everything above is <i>service-level</i> — it applies to every layer added later. Layer-specific bits (layer name, field aliases, hide PII, layer access overrides) come on the next step.
            </Callout>
          </div>

          <div className="col">
            <div className="card">
              <h3>What gets created</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><span className="muted" style={{flex:1}}>Service</span><span className="mono">parcels-fs (new)</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Folder</span><span className="mono">public</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Esri catalog entry</span><Badge kind="info">opt-in · checked</Badge></div>
              </div>
              <Callout kind="info" style={{marginTop:6}}>
                Service is born empty. parcels_2024 will become its first <b>layer 0</b> on the next step.
              </Callout>
            </div>
            <div className="card">
              <h3>URL preview</h3>
              <div style={{padding:'6px 8px', background:'#fafafa', border:'1px solid #e4e4e4', borderRadius:4, fontFamily:'var(--mono)', fontSize:10.5}}>
                https://honua.example.gov/public/parcels-fs/FeatureServer
              </div>
              <Btn ghost sm>⧉ Copy</Btn>
            </div>
            <Ann>defaults are picked from your source. you'll rarely need to touch most fields.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>Cancel</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Layer →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- STEP 3: LAYER — bind existing resource ---------- */
function WizQuickPublishLayer() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Publish service']} />
      <div className="wiz">
        <Stepper steps={['Service','Layer','Review']} on={1} />
        <ModeBar />

        {/* Service context (no Source here — source is part of "create new resource") */}
        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5, flexWrap:'wrap'}}>
          <span style={{color:'#666'}}>🗀</span><b>public</b>
          <span style={{color:'#bbb'}}>›</span>
          <span style={{color:'#666'}}>◈</span><b>public-works-fs</b>
          <span className="mono">FeatureServer · CRS 4326 · public read</span>
          <div style={{flex:1}}/>
          <span className="muted" style={{fontSize:11}}>this layer will become <span className="mono">layer 8</span></span>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, overflow:'auto'}}>
          <div>
            <div className="row" style={{marginBottom:6, alignItems:'baseline'}}>
              <h2 style={{margin:0, font:'600 16px var(--ui)'}}>What does this layer expose?</h2>
              <span className="muted" style={{fontSize:10.5, marginLeft:8}}>a layer is backed by exactly one Data Resource</span>
            </div>

            {/* Mode: bind existing vs create new */}
            <div style={{display:'inline-flex', border:'1.2px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:11, marginBottom:12}}>
              <div style={{padding:'6px 14px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Bind existing resource</div>
              <div style={{padding:'6px 14px', background:'#fff', color:'#666'}}>Create new resource</div>
            </div>

            <Callout kind="info" style={{marginBottom:10}}>
              <b>One resource, many layers.</b> The same Data Resource can back layers in multiple services (e.g. <span className="mono">parcels_2024</span> appears as FeatureServer/0 here AND as MapServer/2 in public-works-ms). All those layers share the same canonical fields and metadata.
            </Callout>

            {/* Resource picker */}
            <div className="row" style={{marginBottom:6}}>
              <FiltChip on x>compatible · Polygon, Point, Line</FiltChip>
              <FiltChip>not yet in this service</FiltChip>
              <FiltChip>my resources</FiltChip>
              <input className="inp" style={{width:200, height:22, marginLeft:'auto'}} placeholder="Filter 128 resources…" readOnly />
            </div>

            <div style={{border:'1px solid #e4e4e4', borderRadius:6, overflow:'hidden'}}>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th style={{width:24}}></th>
                  <th>Resource</th><th>Type</th><th>Features</th><th>Already in this service?</th><th>Used in N layers</th>
                </tr></thead>
                <tbody>
                  <tr className="sel"><td><input type="radio" readOnly defaultChecked /></td>
                    <td><span style={{color:'var(--pencil)'}}>◇</span> <b>parcels_2024</b></td>
                    <td><span className="tag">Polygon</span></td>
                    <td className="num mono">1.28M</td>
                    <td><span className="muted">no</span></td>
                    <td className="mono">3 <span className="muted">(in other services)</span></td>
                  </tr>
                  <tr><td><input type="radio" readOnly /></td>
                    <td><span style={{color:'var(--pencil)'}}>◇</span> <b>fire_observations</b></td>
                    <td><span className="tag">Point</span></td>
                    <td className="num mono">2.1M</td>
                    <td><span className="muted">no</span></td>
                    <td className="mono">0 <span className="muted">(draft)</span></td>
                  </tr>
                  <tr><td><input type="radio" readOnly /></td>
                    <td><span style={{color:'var(--pencil)'}}>◇</span> <b>watersheds_v3</b></td>
                    <td><span className="tag">Polygon</span></td>
                    <td className="num mono">18k</td>
                    <td><span className="muted">no</span></td>
                    <td className="mono">1</td>
                  </tr>
                  <tr><td><input type="radio" readOnly /></td>
                    <td><span style={{color:'var(--pencil)'}}>◇</span> <b>air_quality_obs</b></td>
                    <td><span className="tag">Point</span></td>
                    <td className="num mono">14.8M</td>
                    <td><Badge kind="ok">layer 6</Badge></td>
                    <td className="mono">2</td>
                  </tr>
                  <tr><td><input type="radio" readOnly /></td>
                    <td><span style={{color:'var(--pencil)'}}>◇</span> <b>census_blocks</b></td>
                    <td><span className="tag">Polygon</span></td>
                    <td className="num mono">8.1M</td>
                    <td><span className="muted">no</span></td>
                    <td className="mono">1</td>
                  </tr>
                  <tr style={{opacity:0.55}}><td><input type="radio" readOnly disabled /></td>
                    <td><span style={{color:'#aaa'}}>◇</span> land_cover_2024</td>
                    <td><span className="tag">Raster</span></td>
                    <td className="num mono">—</td>
                    <td className="muted">no</td>
                    <td className="muted">not compatible · raster</td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div className="row" style={{marginTop:8, fontSize:11}}>
              <Btn ghost sm>+ Create new resource from connection</Btn>
              <Btn ghost sm>+ Create from file</Btn>
              <Btn ghost sm>+ Migrate remote service</Btn>
              <span className="muted" style={{marginLeft:'auto'}}>any of these jump to "Create new resource" mode</span>
            </div>

            {/* Layer-specific settings */}
            <h3 style={{margin:'18px 0 4px', font:'600 13px var(--ui)'}}>Layer-specific settings</h3>
            <div className="muted" style={{fontSize:11, marginBottom:8}}>
              These ride on top of the resource's canonical fields/metadata. Use them to brand this particular layer slot — none of it changes the resource itself.
            </div>

            <div className="card" style={{marginBottom:10}}>
              <h3>Identity</h3>
              <div style={{display:'grid',gridTemplateColumns:'1fr 1fr', gap:10}}>
                <Field label="Layer name" hint="what consumers see in QGIS, Pro, etc."><Inp value="Parcels" /></Field>
                <Field label="Layer ID" hint="next slot in this service">
                  <div style={{height:26, border:'1px dashed #c0c0c0', borderRadius:4, background:'#fafafa', padding:'0 8px', display:'flex',alignItems:'center', font:'11px var(--mono)', color:'#666'}}>
                    8<span style={{flex:1}}/><span style={{color:'#bbb',fontSize:9.5}}>auto</span>
                  </div>
                </Field>
                <Field label="Display field"><Sel value="parcel_id (inherits from resource)" /></Field>
                <Field label="Layer description"><Inp value="(optional)" /></Field>
              </div>
            </div>

            <div className="card" style={{padding:0, marginBottom:10}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
                <h3>Fields exposed in this layer</h3>
                <span className="muted" style={{fontSize:11, marginLeft:8}}>resource has 24 fields · pick &amp; rename for this slot</span>
                <div style={{flex:1}}/>
                <Btn ghost sm>Show all 24</Btn>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th style={{width:24}}></th>
                  <th>Resource field</th><th>Alias here</th><th>Role</th>
                </tr></thead>
                <tbody>
                  <tr><td><input type="checkbox" readOnly defaultChecked /></td><td className="mono"><b>gid</b></td><td>OBJECTID</td><td><Badge kind="accent">Primary ID</Badge></td></tr>
                  <tr><td><input type="checkbox" readOnly defaultChecked /></td><td className="mono">parcel_id</td><td>Parcel ID</td><td>Display</td></tr>
                  <tr><td><input type="checkbox" readOnly defaultChecked /></td><td className="mono">area_m2</td><td>Area (m²)</td><td>—</td></tr>
                  <tr style={{background:'#fff7e6'}}><td><input type="checkbox" readOnly /></td><td className="mono">owner_name</td><td className="muted">— hidden —</td><td><Badge kind="warn">PII flagged on resource</Badge></td></tr>
                  <tr><td colSpan="4" className="muted" style={{textAlign:'center',padding:8,fontSize:10.5}}>+ 20 more · default = inherit from resource</td></tr>
                </tbody>
              </table>
            </div>

            <div className="card">
              <h3>Per-layer overrides</h3>
              <div className="muted" style={{fontSize:11, marginBottom:6}}>
                Usually leave at "inherit". Override only when this layer needs to differ from the service.
              </div>
              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
                <Field label="Anonymous access" hint="service default: public read"><Sel value="Inherit (public read)" /></Field>
                <Field label="Max record count" hint="service default: 5,000"><Sel value="Inherit (5,000)" /></Field>
                <Field label="Cache TTL" hint="service default: 30 min"><Sel value="Inherit (30 min)" /></Field>
                <Field label="Geometry simplification at small scales"><Sel value="auto (recommended)" /></Field>
              </div>
            </div>
          </div>

          <div className="col">
            <div className="card">
              <h3>Picked resource</h3>
              <dl className="kv">
                <dt>Resource</dt><dd className="mono"><span style={{color:'var(--pencil)'}}>◇</span> parcels_2024</dd>
                <dt>Geometry</dt><dd>MultiPolygon · 4326</dd>
                <dt>Features</dt><dd className="mono">1,284,021</dd>
                <dt>Fields</dt><dd>24</dd>
                <dt>Last refreshed</dt><dd>2m ago · nightly</dd>
              </dl>
              <Btn ghost sm>Open resource ↗</Btn>
            </div>

            <div className="card" style={{background:'#fffdf3', borderLeft:'3px solid var(--accent-deep)'}}>
              <h3>Also published as</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><Badge kind="ok">live</Badge><span className="mono" style={{marginLeft:6,fontSize:10}}>public-works-ms / layer 2</span></div>
                <div className="row"><Badge kind="ok">live</Badge><span className="mono" style={{marginLeft:6,fontSize:10}}>features-public / parcels_2024</span></div>
                <div className="row"><Badge kind="warn">stale</Badge><span className="mono" style={{marginLeft:6,fontSize:10}}>tiles-public / parcels_2024</span></div>
              </div>
              <div className="muted" style={{fontSize:10.5, marginTop:6}}>Adding here doesn't change those — each is its own publication slot.</div>
            </div>

            <Ann red>same resource, different layer ID + alias + access. that's the layer's job.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Service</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Review →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- STEP 3 (alt): LAYER — create new resource ---------- */
function WizQuickPublishLayerNew() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Publish service']} />
      <div className="wiz">
        <Stepper steps={['Service','Layer','Review']} on={1} />
        <ModeBar />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5, flexWrap:'wrap'}}>
          <span style={{color:'#666'}}>🗀</span><b>public</b>
          <span style={{color:'#bbb'}}>›</span>
          <span style={{color:'#666'}}>◈</span><b>public-works-fs</b>
          <div style={{flex:1}}/>
          <span className="muted" style={{fontSize:11}}>creating a new Data Resource for this layer</span>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24, overflow:'auto'}}>
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>What does this layer expose?</h2>

            <div style={{display:'inline-flex', border:'1.2px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:11, marginBottom:14}}>
              <div style={{padding:'6px 14px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Bind existing resource</div>
              <div style={{padding:'6px 14px', background:'var(--accent)', fontWeight:600}}>Create new resource</div>
            </div>

            <Callout kind="info" style={{marginBottom:10}}>
              <b>New resource = new canonical home.</b> We'll create a Data Resource alongside the layer slot. The resource owns fields and metadata; the layer slot reuses it.
            </Callout>

            {/* SOURCE PICKER */}
            <div className="card" style={{marginBottom:10}}>
              <h3>1. Where's the data?</h3>
              <div style={{display:'inline-flex', border:'1px solid #d0d0d0', borderRadius:5, overflow:'hidden', fontSize:11, marginBottom:8}}>
                <div style={{padding:'4px 10px', background:'#fffae0', fontWeight:600, borderRight:'1px solid #d0d0d0'}}>From a table</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #d0d0d0'}}>From a file</div>
                <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>Migrate remote service</div>
              </div>
              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
                <Field label="Connection"><Sel value="prod-postgis" /></Field>
                <Field label="Schema · table"><Sel value="public.parcels_2024" /></Field>
                <Field label="Primary ID column" hint="auto-detected"><Sel value="gid" /></Field>
                <Field label="Geometry column"><Sel value="geom · MultiPolygon · 4326" /></Field>
              </div>
            </div>

            {/* RESOURCE NAMING */}
            <div className="card" style={{marginBottom:10}}>
              <h3>2. Name the new resource</h3>
              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
                <Field label="Resource name (canonical)" hint="how it'll appear across Honua">
                  <Inp mono value="parcels_2024" />
                </Field>
                <Field label="Resource title" hint="human-friendly">
                  <Inp value="Tax Parcels (FY 2024)" />
                </Field>
              </div>
              <Callout kind="info">
                <b>This resource will be reusable.</b> Once created, you can publish it as a layer in other services too (OGC API, MapServer, WMTS) without re-doing the source / fields / metadata work.
              </Callout>
            </div>

            {/* LAYER NAME */}
            <div className="card">
              <h3>3. Name the layer (this service only)</h3>
              <div style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap:10}}>
                <Field label="Layer name" hint="how consumers see it in this service"><Inp value="Parcels" /></Field>
                <Field label="Layer ID">
                  <div style={{height:26, border:'1px dashed #c0c0c0', borderRadius:4, background:'#fafafa', padding:'0 8px', display:'flex',alignItems:'center', font:'11px var(--mono)', color:'#666'}}>
                    8<span style={{flex:1}}/><span style={{color:'#bbb',fontSize:9.5}}>auto</span>
                  </div>
                </Field>
              </div>
              <div className="muted" style={{fontSize:11}}>
                The resource name and the layer name don't have to match. Resource = canonical identity. Layer = consumer-facing label in this service.
              </div>
            </div>
          </div>

          <div className="col">
            <div className="card">
              <h3>What gets created</h3>
              <ol style={{margin:'0 0 0 16px', padding:0, fontSize:11.5, lineHeight:1.7}}>
                <li>Data Resource <span className="mono">◇ parcels_2024</span> <span className="tag">new</span></li>
                <li>Layer slot <span className="mono">public-works-fs / layer 8</span> <span className="tag">new</span></li>
                <li>Auto Esri catalog entry <span className="tag">auto</span></li>
              </ol>
            </div>

            <Callout kind="info">
              <b>Defaults.</b> Detected fields, geometry, CRS, primary key all flow in from the source. You'll be able to tune fields, metadata, presentation on the resource page after publish.
            </Callout>

            <Ann>field aliases &amp; hidden fields are layer-level. canonical fields and PII flags are resource-level.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Service</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Review →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- STEP 4: REVIEW ---------- */
function WizQuickPublishReview() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Publish service']} />
      <div className="wiz">
        <Stepper steps={['Service','Layer','Review']} on={2} />
        <ModeBar />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <span style={{color:'#666'}}>🗀</span><b>public</b>
          <span style={{color:'#bbb'}}>›</span>
          <span style={{color:'#666'}}>◈</span><b>public-works-fs</b>
          <span style={{color:'#bbb'}}>›</span>
          <span style={{color:'#666'}}>◇</span><b>layer 8 · Parcels</b>
          <span className="muted">→ binds resource ◇ parcels_2024 (new)</span>
          <div style={{flex:1}}/>
          <Badge kind="ok">ready</Badge>
        </div>

        <div className="body" style={{display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:24}}>
          <div>
            <h2 style={{margin:'0 0 4px', font:'600 16px var(--ui)'}}>Ready to publish</h2>
            <div className="muted" style={{marginBottom:14, fontSize:11.5}}>
              Three things will be created when you click Publish. Service settings are taken from the existing service unchanged; only the new layer adds to it.
            </div>

            {/* Three creation cards */}
            <div className="col" style={{gap:10}}>
              <div className="card" style={{padding:'10px 12px'}}>
                <div className="row" style={{marginBottom:6}}>
                  <span style={{fontSize:14, color:'var(--pencil)'}}>◇</span>
                  <b>Data Resource · parcels_2024</b>
                  <span className="tag" style={{marginLeft:4}}>new (auto)</span>
                  <div style={{flex:1}}/>
                  <Badge kind="ok">canonical home</Badge>
                </div>
                <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
                  <tbody>
                    <tr><td className="muted" style={{width:120}}>Source</td><td className="mono">prod-postgis / public.parcels_2024</td></tr>
                    <tr><td className="muted">Primary ID</td><td className="mono">gid</td></tr>
                    <tr><td className="muted">Geometry</td><td className="mono">geom · MultiPolygon · 4326</td></tr>
                    <tr><td className="muted">Fields detected</td><td>24 (1 PII flagged)</td></tr>
                    <tr><td className="muted">Refresh</td><td>nightly from source</td></tr>
                  </tbody>
                </table>
              </div>

              <div style={{textAlign:'center', color:'#888', fontSize:14}}>↓ binds to ↓</div>

              <div className="card" style={{padding:'10px 12px'}}>
                <div className="row" style={{marginBottom:6}}>
                  <span style={{fontSize:14}}>◈</span>
                  <b>public-works-fs / layer 8 · "Parcels"</b>
                  <span className="tag" style={{marginLeft:4}}>new slot in existing service</span>
                  <div style={{flex:1}}/>
                  <Badge kind="ok">publication</Badge>
                </div>
                <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
                  <tbody>
                    <tr><td className="muted" style={{width:120}}>URL</td><td className="mono">https://honua.example.gov/public/pw/FeatureServer/8</td></tr>
                    <tr><td className="muted">Service settings</td><td className="muted">inherits from public-works-fs (CRS 4326, capabilities Query+Extract, cache 30 min…)</td></tr>
                    <tr><td className="muted">Layer-specific</td><td>23 fields exposed · 5 aliased · owner_name hidden</td></tr>
                    <tr><td className="muted">Style</td><td>auto class-breaks on <span className="mono">area_m2</span></td></tr>
                  </tbody>
                </table>
              </div>

              <div style={{textAlign:'center', color:'#888', fontSize:14}}>↓ opt-in (checked) ↓</div>

              <div className="card" style={{padding:'10px 12px', borderLeft:'3px solid var(--pencil)'}}>
                <div className="row" style={{marginBottom:6}}>
                  <span style={{fontSize:14, color:'var(--pencil)'}}>▤</span>
                  <b>Esri catalog entry</b>
                  <span className="tag" style={{marginLeft:4}}>opt-in · default on</span>
                  <div style={{flex:1}}/>
                  <a style={{fontSize:11, color:'var(--pencil)', textDecoration:'underline dotted', cursor:'pointer'}}>Open in Esri catalog ↗</a>
                </div>
                <label className="row" style={{gap:6, padding:'4px 8px', background:'#fafafa', borderRadius:4, marginBottom:6, fontSize:11}}>
                  <input type="checkbox" readOnly defaultChecked />
                  <span style={{flex:1}}>Register this service in the Esri catalog</span>
                  <span className="muted" style={{fontSize:10}}>uncheck to skip · service still works</span>
                </label>
                <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
                  <tbody>
                    <tr><td className="muted" style={{width:120}}>Catalog URL</td><td className="mono">https://honua.example.gov/catalog/item/new</td></tr>
                    <tr><td className="muted">Title</td><td>Parcels (from layer name)</td></tr>
                    <tr><td className="muted">Thumbnail</td><td className="muted">auto-rendered from map preview</td></tr>
                    <tr><td className="muted">Stays in sync</td><td>yes — when you update the service, the catalog entry updates</td></tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>

          <div className="col">
            <Callout kind="info">
              <b>Service settings are unchanged.</b> You reused public-works-fs, so its CRS, capabilities, cache, anonymous access, and output formats all apply. Only the layer adds new state.
            </Callout>

            <div className="card" style={{padding:'8px 10px',background:'#fafafa'}}>
              <h3>Final URL</h3>
              <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #d8d8d8', borderRadius:4, fontFamily:'var(--mono)', fontSize:10.5, marginBottom:6}}>
                /public/pw/FeatureServer/8
              </div>
              <div className="row" style={{gap:4}}>
                <Btn ghost sm>⧉ Copy URL</Btn>
                <Btn ghost sm>🗺 Preview map</Btn>
              </div>
            </div>

            <div className="card">
              <h3>After publish, you can:</h3>
              <ul style={{margin:'4px 0 0 16px', padding:0, fontSize:11, lineHeight:1.55}}>
                <li>Tune metadata on the resource</li>
                <li>Add this resource as a layer in <i>another</i> service (OGC API, MapServer, WMTS)</li>
                <li>Override style for any specific service</li>
                <li>Change service-level settings on public-works-fs (affects all layers)</li>
              </ul>
            </div>

            <Ann red>publish ≠ moved to prod. it just turns the layer slot on. unpublish reverses it in one click.</Ann>
          </div>
        </div>

        <div className="foot">
          <Btn ghost>← Layer</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="a">Publish + open resource →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { WizQuickPublishService, WizQuickPublishNewService, WizQuickPublishLayer, WizQuickPublishLayerNew, WizQuickPublishReview });
