// Create resource wizards: from Table, from File, Import remote service (Esri / OGC / WMS / WFS)

function WizFromTable() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Data resources','Create','From table']} />
      <div className="wiz">
        <Stepper steps={['Source','Inspect','Identity','Metadata','Access','Publish','Review']} on={1} />
        <div className="body" style={{display:'grid',gridTemplateColumns:'1.4fr 1fr',gap:24}}>
          <div>
            <h2 style={{margin:'0 0 4px',font:'600 16px var(--ui)'}}>Inspect the source</h2>
            <div className="muted" style={{marginBottom:14,fontSize:11.5}}>
              prod-postgis · <span className="mono">public.parcels_2024</span> · 1,284,021 rows
            </div>

            <div className="card" style={{padding:0,marginBottom:12}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
                <h3>Detected fields · 24</h3>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th>Field</th><th>Type</th><th>Role</th><th>PK</th><th>Geometry</th><th>Notes</th>
                </tr></thead>
                <tbody>
                  <tr><td className="mono"><b>gid</b></td><td className="mono">int8</td><td><Badge kind="accent">Primary ID</Badge></td><td>★</td><td></td><td className="muted">auto-detected</td></tr>
                  <tr><td className="mono">parcel_id</td><td className="mono">text</td><td>—</td><td></td><td></td><td className="muted">candidate for display</td></tr>
                  <tr><td className="mono">owner_name</td><td className="mono">text</td><td>—</td><td></td><td></td><td className="muted">PII suggested · review on Fields</td></tr>
                  <tr><td className="mono">area_m2</td><td className="mono">float8</td><td>—</td><td></td><td></td><td></td></tr>
                  <tr><td className="mono">use_code</td><td className="mono">text</td><td>—</td><td></td><td></td><td className="muted">12 distinct values · domain?</td></tr>
                  <tr><td className="mono">last_assessment</td><td className="mono">date</td><td>—</td><td></td><td></td><td className="muted">candidate temporal field</td></tr>
                  <tr><td className="mono"><b>geom</b></td><td className="mono">geometry</td><td><Badge kind="accent">Geometry</Badge></td><td></td><td>MultiPolygon · 4326</td><td></td></tr>
                </tbody>
              </table>
              <div style={{padding:'6px 12px',background:'#fafafa',fontSize:10.5,color:'#777'}}>
                + 17 more fields. You can change roles, aliases, and exposure on the Fields tab after creation.
              </div>
            </div>

            <div className="card">
              <h3>What we found</h3>
              <div className="col" style={{gap:4,fontSize:11.5}}>
                <div className="row"><Badge kind="ok">✓</Badge> <span>Primary key detected: <span className="mono">gid</span></span></div>
                <div className="row"><Badge kind="ok">✓</Badge> <span>Geometry column: <span className="mono">geom</span> (MultiPolygon, EPSG:4326)</span></div>
                <div className="row"><Badge kind="ok">✓</Badge> <span>Spatial index present</span></div>
                <div className="row"><Badge kind="warn">!</Badge> <span>Possible PII: <span className="mono">owner_name</span>. Marked sensitive by default.</span></div>
                <div className="row"><Badge kind="warn">!</Badge> <span><span className="mono">use_code</span> has 12 distinct values — consider a domain.</span></div>
              </div>
            </div>
          </div>

          <div className="col">
            <div className="card" style={{padding:0}}>
              <Ph style={{borderRadius:0,border:0,borderBottom:'1px dashed #c5c5c5',minHeight:200}}>
                preview map · sampled 1,000 features · 4326
              </Ph>
              <div style={{padding:'8px 12px',fontSize:11}}>
                <div className="row"><span className="muted" style={{flex:1}}>Extent</span><span className="mono">-124.4 32.5 / -114.1 42.0</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Storage</span><span className="mono">~1.4 GB</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Estimated row count</span><span className="mono">1,284,021</span></div>
              </div>
            </div>
            <Callout kind="info">Honua won't copy this data. The resource binds to the live table and refreshes on a schedule you set in step 6.</Callout>
            <Ann>this is a referenced source. for files (next wizard) we materialise.</Ann>
          </div>
        </div>
        <div className="foot">
          <Btn ghost>← Source</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Identity →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function WizFromFile() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Data resources','Create','From file']} />
      <div className="wiz">
        <Stepper steps={['Upload','Scan','Layers','Inspect','Identity','Metadata','Access','Import']} on={2} />
        <div className="body" style={{display:'grid',gridTemplateColumns:'1.4fr 1fr',gap:24}}>
          <div>
            <h2 style={{margin:'0 0 4px',font:'600 16px var(--ui)'}}>Pick which layers to import</h2>
            <div className="muted" style={{marginBottom:14,fontSize:11.5}}>
              parcels_export_2024.gdb.zip · 248 MB · FileGDB · 4 layers + 1 standalone table
            </div>

            <div className="card" style={{padding:0,marginBottom:12}}>
              <div style={{padding:'8px 12px',borderBottom:'1px solid #e4e4e4',display:'flex',alignItems:'center'}}>
                <h3>Contents</h3>
                <div style={{flex:1}}/>
                <span className="muted" style={{fontSize:11}}>3 of 5 selected</span>
              </div>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th style={{width:24}}><input type="checkbox" readOnly defaultChecked /></th>
                  <th>Name</th><th>Type</th><th>Geometry</th><th>CRS</th><th className="num">Rows</th><th>Warnings</th>
                </tr></thead>
                <tbody>
                  <tr className="sel"><td><input type="checkbox" readOnly defaultChecked /></td><td className="mono"><b>parcels</b></td><td>Feature class</td><td>MultiPolygon</td><td className="mono">2227</td><td className="num mono">1.28M</td><td><Badge kind="warn">reprojected from 2227 → ask</Badge></td></tr>
                  <tr className="sel"><td><input type="checkbox" readOnly defaultChecked /></td><td className="mono"><b>parcel_centroids</b></td><td>Feature class</td><td>Point</td><td>4326</td><td className="num mono">1.28M</td><td className="muted">—</td></tr>
                  <tr className="sel"><td><input type="checkbox" readOnly defaultChecked /></td><td className="mono"><b>owner_lookup</b></td><td>Standalone table</td><td>—</td><td>—</td><td className="num mono">812k</td><td className="muted">no geometry</td></tr>
                  <tr><td><input type="checkbox" readOnly /></td><td className="mono">parcel_history</td><td>Feature class</td><td>MultiPolygon</td><td>2227</td><td className="num mono">4.1M</td><td className="muted">skip · archive</td></tr>
                  <tr><td><input type="checkbox" readOnly /></td><td className="mono">audit_log</td><td>Standalone table</td><td>—</td><td>—</td><td className="num mono">10.4M</td><td className="muted">skip</td></tr>
                </tbody>
              </table>
            </div>

            <div className="card">
              <h3>Import strategy</h3>
              <div className="col" style={{gap:6,fontSize:11.5}}>
                <label className="row" style={{border:'1.5px solid var(--ink)',borderRadius:4,padding:'8px 10px',background:'#fffae0'}}>
                  <input type="radio" readOnly defaultChecked />
                  <div style={{flex:1}}>
                    <div style={{fontWeight:600}}>Copy into Honua storage</div>
                    <div className="muted">Materialize as managed Postgres tables. Best for ongoing publishing.</div>
                  </div>
                  <span className="tag">recommended</span>
                </label>
                <label className="row" style={{border:'1px solid #d0d0d0',borderRadius:4,padding:'8px 10px'}}>
                  <input type="radio" readOnly />
                  <div style={{flex:1}}>
                    <div style={{fontWeight:600}}>Register as external file</div>
                    <div className="muted">No copy. Read on every request. Good for big static rasters.</div>
                  </div>
                </label>
                <label className="row" style={{border:'1px solid #d0d0d0',borderRadius:4,padding:'8px 10px'}}>
                  <input type="radio" readOnly />
                  <div style={{flex:1}}>
                    <div style={{fontWeight:600}}>Stage for review</div>
                    <div className="muted">Save the upload, scan it, create drafts only. No data copy yet.</div>
                  </div>
                </label>
              </div>
            </div>
          </div>

          <div className="col">
            <div className="card">
              <h3>Destination</h3>
              <Field label="Connection" hint="where materialized tables will live"><Sel value="prod-postgis · schema honua_imports" /></Field>
              <Field label="Naming"><Sel value="Keep source names + suffix _2024" /></Field>
              <div className="muted" style={{fontSize:11}}>
                Will create: <span className="mono">parcels_2024</span>, <span className="mono">parcel_centroids_2024</span>, <span className="mono">owner_lookup_2024</span>.
              </div>
            </div>
            <Callout kind="warn">
              <b>Reprojection needed.</b> Source CRS 2227 (CA State Plane). We'll reproject geometries to 4326 on import. Original CRS is preserved in <span className="mono">source_crs</span> field.
            </Callout>
            <div className="card" style={{gap:4}}>
              <h3>Estimate</h3>
              <div className="row"><span className="muted" style={{flex:1,fontSize:11}}>Storage</span><span className="mono">~ 2.1 GB</span></div>
              <div className="row"><span className="muted" style={{flex:1,fontSize:11}}>Time</span><span className="mono">~ 8 min</span></div>
              <div className="row"><span className="muted" style={{flex:1,fontSize:11}}>Rows total</span><span className="mono">3.37 M</span></div>
            </div>
          </div>
        </div>
        <div className="foot">
          <Btn ghost>← Scan</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Inspect →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function WizImportEsri() {
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Imports','Remote service']} />
      <div className="wiz">
        <Stepper steps={['URL','Discover','Select layers','Map','Destination','Access','Run']} on={2} />

        <div style={{padding:'10px 18px', background:'#fffdf3', borderBottom:'1px solid #e4e4e4', fontSize:11.5}}>
          <b>Migration mode.</b> This brings data <i>off</i> of an existing Esri / OGC / WMS / WFS service and into Honua-managed storage as new Data Resources. One-time copy — Honua doesn't proxy the remote service or hold its credentials. To pull fresher data later, run this wizard again.
        </div>

        <div className="body" style={{display:'grid',gridTemplateColumns:'1.4fr 1fr',gap:24}}>
          <div>
            <div className="card" style={{padding:'10px 12px', marginBottom:12, background:'#fafafa'}}>
              <div className="row"><span className="tag">FeatureServer</span>
                <span className="mono" style={{fontSize:11}}>https://services.example.com/arcgis/rest/services/Parcels/FeatureServer</span>
                <div style={{flex:1}}/>
                <Badge kind="ok">discovered</Badge>
              </div>
              <div className="muted" style={{fontSize:11,marginTop:4}}>
                "Statewide Parcels v3" · ArcGIS 11.2 · public · WGS84 · 7 layers
              </div>
            </div>

            <h2 style={{margin:'0 0 4px',font:'600 16px var(--ui)'}}>Pick layers to import</h2>
            <div className="muted" style={{marginBottom:14,fontSize:11.5}}>You can rename and remap later. Partial success is fine — failed layers won't block the others.</div>

            <div style={{border:'1px solid #e4e4e4',borderRadius:6,overflow:'hidden'}}>
              <table className="tbl tbl--cmpt">
                <thead><tr>
                  <th style={{width:24}}><input type="checkbox" readOnly defaultChecked /></th>
                  <th>#</th><th>Title</th><th>Type</th><th>Geometry</th><th className="num">Features</th><th>Cap.</th><th>Warnings</th>
                </tr></thead>
                <tbody>
                  <tr className="sel"><td><input type="checkbox" readOnly defaultChecked /></td><td>0</td><td><b>Parcels</b></td><td>Feature layer</td><td>MultiPolygon</td><td className="num mono">1.28M</td><td><span className="tag">Q E</span></td><td></td></tr>
                  <tr className="sel"><td><input type="checkbox" readOnly defaultChecked /></td><td>1</td><td><b>Tax assessment events</b></td><td>Standalone table</td><td>—</td><td className="num mono">3.4M</td><td><span className="tag">Q</span></td><td></td></tr>
                  <tr className="sel"><td><input type="checkbox" readOnly defaultChecked /></td><td>2</td><td><b>Parcel centroids</b></td><td>Feature layer</td><td>Point</td><td className="num mono">1.28M</td><td><span className="tag">Q</span></td><td></td></tr>
                  <tr><td><input type="checkbox" readOnly /></td><td>3</td><td>Historic parcels</td><td>Feature layer</td><td>MultiPolygon</td><td className="num mono">4.1M</td><td><span className="tag">Q</span></td><td><Badge kind="warn">archived</Badge></td></tr>
                  <tr><td><input type="checkbox" readOnly /></td><td>4</td><td>Imagery (footprints)</td><td>Feature layer</td><td>Polygon</td><td className="num mono">42k</td><td><span className="tag">Q</span></td><td className="muted">requires auth</td></tr>
                  <tr><td><input type="checkbox" readOnly /></td><td>5</td><td>Editing audit</td><td>Standalone table</td><td>—</td><td className="num mono">14M</td><td><span className="tag">Q</span></td><td><Badge kind="warn">slow</Badge></td></tr>
                  <tr><td><input type="checkbox" readOnly /></td><td>6</td><td>Service metadata</td><td>Feature layer</td><td>Polygon</td><td className="num mono">1</td><td><span className="tag">Q</span></td><td className="muted">cover sheet</td></tr>
                </tbody>
              </table>
            </div>

            <div className="row" style={{marginTop:10,fontSize:11}}>
              <span className="muted">Selected:</span>
              <b>3 of 7 layers</b>
              <span className="muted">·</span>
              <span>5.96 M features</span>
              <span className="muted">·</span>
              <span>est. 12 min</span>
            </div>
          </div>
          <div className="col">
            <div className="card">
              <h3>Mapping preview</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row">
                  <span className="mono" style={{flex:1}}>Parcels</span><span style={{color:'#bbb'}}>→</span>
                  <span className="mono">honua / parcels_v3</span>
                </div>
                <div className="row">
                  <span className="mono" style={{flex:1}}>Tax assessment events</span><span style={{color:'#bbb'}}>→</span>
                  <span className="mono">honua / tax_events</span>
                </div>
                <div className="row">
                  <span className="mono" style={{flex:1}}>Parcel centroids</span><span style={{color:'#bbb'}}>→</span>
                  <span className="mono">honua / parcel_centroids</span>
                </div>
              </div>
              <Btn sm>Edit names</Btn>
            </div>
            <div className="card">
              <h3>Provenance</h3>
              <div className="col" style={{gap:4,fontSize:11}}>
                <div className="row"><span className="muted" style={{flex:1}}>Source URL</span><span className="mono" style={{fontSize:10}}>Parcels / FeatureServer</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Owner</span><span>state-gis</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Licence</span><span>CC-BY 4.0</span></div>
                <div className="row"><span className="muted" style={{flex:1}}>Last updated</span><span>2026-03-14</span></div>
              </div>
            </div>
            <Ann>partial success: keep one failed layer from hiding 6 successful ones.</Ann>
          </div>
        </div>
        <div className="foot">
          <Btn ghost>← Discover</Btn>
          <div className="row">
            <Btn ghost>Save as draft</Btn>
            <Btn kind="p">Continue · Map →</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { WizFromTable, WizFromFile, WizImportEsri });
