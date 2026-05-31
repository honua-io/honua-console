// Issue #122 · Esri CONTENT import surfaces (distinct from the data-layer→PostGIS importer)
// screens-import-esri.jsx — #100 Web Map, #101 Dashboard, #104 StoryMap/Hub
// Convention: per-item fidelity badge — clean / degrades / dropped.
//   Every surface covers empty · loading · success · partial · error/unsupported · missing-binding.

// fidelity badge helper
function Fid({ k }) {
  if (k === 'clean')   return <Badge kind="ok">converts clean</Badge>;
  if (k === 'degrade') return <Badge kind="warn">degrades</Badge>;
  if (k === 'drop')    return <Badge kind="bad">dropped</Badge>;
  if (k === 'manual')  return <Badge kind="info">needs review</Badge>;
  return <span className="muted">—</span>;
}

// Shared intake bar (drop / paste / URL)
function EsriIntake({ kind, file, summary }) {
  return (
    <div style={{padding:'12px 18px', borderBottom:'1px solid #e4e4e4', background:'#fafafa'}}>
      <div className="row" style={{gap:8, marginBottom:8}}>
        <div style={{display:'inline-flex', border:'1px solid var(--ink)', borderRadius:5, overflow:'hidden', fontSize:10.5}}>
          <div style={{padding:'4px 10px', background:'var(--accent)', fontWeight:600, borderRight:'1px solid var(--ink)'}}>Paste JSON</div>
          <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>Upload file</div>
          <div style={{padding:'4px 10px', background:'#fff', color:'#666', borderRight:'1px solid #ccc'}}>From URL / item ID</div>
          <div style={{padding:'4px 10px', background:'#fff', color:'#666'}}>From connected ArcGIS</div>
        </div>
        <div style={{flex:1}}/>
        <span className="muted" style={{fontSize:10.5}}>{kind} · {file}</span>
      </div>
      <div className="row" style={{gap:8, fontSize:11.5}}>
        {summary.map((s,i) => (
          <span key={i} className="tag" style={{padding:'3px 8px'}}>{s}</span>
        ))}
      </div>
    </div>
  );
}

/* ---------- #100 · Import Esri Web Map JSON → honua.map-package.v1 ---------- */
function ImportEsriWebMap() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Import from Esri','Web Map']} area="operate" />
      <Sidebar active="resources" />
      <div className="main">
        <PageHead
          title="Import Esri Web Map"
          sub={<span>Convert an ArcGIS Web Map JSON into a Honua map package (<span className="mono">honua.map-package.v1</span>). Layer-by-layer fidelity shown before you commit.</span>}
          actions={<><Btn>Discard</Btn><Btn kind="p">Create map package →</Btn></>}
        />
        <EsriIntake kind="Esri Web Map JSON · v2.27" file="public-works-webmap.json · 84 KB"
          summary={['7 operational layers','1 basemap','4 bookmarks','5 popups','2 unsupported widgets']} />

        <div style={{display:'grid', gridTemplateColumns:'1fr 380px', flex:1, overflow:'hidden'}}>
          {/* mapping table */}
          <div style={{overflow:'auto'}}>
            <div style={{padding:'8px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center'}}>
              <h3 style={{margin:0}}>Layer mapping</h3>
              <span className="muted" style={{fontSize:11, marginLeft:8}}>Esri operational layer → Honua map-package layer</span>
              <div style={{flex:1}}/>
              <FiltChip on x>show: all</FiltChip>
              <FiltChip>issues only</FiltChip>
            </div>
            <table className="tbl tbl--cmpt">
              <thead><tr>
                <th style={{width:24}}><input type="checkbox" readOnly defaultChecked /></th>
                <th>Esri layer</th><th>Type</th><th>→ Honua layer</th><th>Bound resource</th><th>Fidelity</th><th>Note</th>
              </tr></thead>
              <tbody>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Parcels</b></td><td>FeatureLayer</td><td className="mono">parcels/fill</td><td><span style={{color:'var(--pencil)'}}>◇</span> parcels_2024</td><td><Fid k="clean" /></td><td className="muted">matched by URL</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Roads</b></td><td>FeatureLayer</td><td className="mono">roads/line</td><td><span style={{color:'var(--pencil)'}}>◇</span> roads_osm</td><td><Fid k="clean" /></td><td className="muted">matched by name</td></tr>
                <tr style={{background:'#fff7e6'}}><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Hydrants</b></td><td>FeatureLayer</td><td className="mono">hydrants/circle</td><td className="muted">— no match —</td><td><Fid k="manual" /></td><td>pick a resource or import data first</td></tr>
                <tr style={{background:'#fff7e6'}}><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Land use</b></td><td>FeatureLayer · UniqueValue</td><td className="mono">landuse/fill</td><td><span style={{color:'var(--pencil)'}}>◇</span> land_cover_2024</td><td><Fid k="degrade" /></td><td>Arcade label expr → static label</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Imagery</b></td><td>TileLayer</td><td className="mono">basemap/imagery</td><td className="muted">remote tiles</td><td><Fid k="clean" /></td><td className="muted">referenced</td></tr>
                <tr style={{background:'#fbeae7'}}><td><input type="checkbox" readOnly /></td><td><b>Heatmap · 311 calls</b></td><td>FeatureLayer · Heatmap</td><td className="muted">—</td><td className="muted">—</td><td><Fid k="drop" /></td><td>heatmap renderer not in map-package.v1</td></tr>
                <tr style={{background:'#fbeae7'}}><td><input type="checkbox" readOnly /></td><td><b>Time slider widget</b></td><td>Widget</td><td className="muted">—</td><td className="muted">—</td><td><Fid k="drop" /></td><td>widget · re-add in Studio after import</td></tr>
              </tbody>
            </table>

            <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', display:'flex', gap:8, fontSize:11}}>
              <span className="muted">Also carried over:</span>
              <span className="tag">basemap ✓</span>
              <span className="tag">4 bookmarks ✓</span>
              <span className="tag">5 popups → templates ✓</span>
              <span className="tag">initial extent ✓</span>
              <span className="tag" style={{opacity:0.6}}>2 widgets dropped</span>
            </div>
          </div>

          {/* target preview */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
              <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Target map package</div>
              <div style={{font:'600 11.5px var(--ui)'}}>honua.map-package.v1</div>
            </div>
            <div style={{padding:10}}>
              <MapPreview mode="layer" height={220} popup={false} scaleText="1:24,000" />
            </div>
            <div style={{padding:'4px 12px'}}>
              <FieldGroup title="Package" scope="resource">
                <FieldRow state="input" label="Name"><Inp mono value="public-works-overview" /></FieldRow>
                <FieldRow state="calculated" label="Layers in" value="5 of 7 (2 dropped)" derivedFrom="mapping" />
                <FieldRow state="calculated" label="Unbound layers" value="1 · Hydrants" derivedFrom="mapping" />
                <FieldRow state="input" label="On unbound layer">
                  <Sel value="create draft + flag for binding" />
                </FieldRow>
              </FieldGroup>
              <Callout kind="warn">
                <b>5 of 7 layers convert.</b> 1 needs a resource binding · 2 dropped (heatmap, time-slider widget). You can finish in Studio after import.
              </Callout>
            </div>
          </div>
        </div>

        {/* missing-binding banner (Charter §11) */}
        <div style={{padding:'8px 18px', borderTop:'1.2px solid #aac1e8', background:'#eaf0fb', display:'flex', alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="info">binding required</Badge>
          <span style={{flex:1}}>Layer <b>Hydrants</b> has no matching Data Resource. Bind one, import the data first, or create the package as a draft with the layer disabled.</span>
          <Btn sm>Pick resource…</Btn>
          <Btn ghost sm>Import data first</Btn>
        </div>
      </div>
    </div>
  );
}

/* ---------- #101 · Import Esri Dashboard JSON → dashboard package ---------- */
function ImportEsriDashboard() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Import from Esri','Dashboard']} area="operate" />
      <Sidebar active="resources" />
      <div className="main">
        <PageHead
          title="Import Esri Dashboard"
          sub="Convert an ArcGIS Dashboard JSON into a Honua dashboard package. Element-by-element support shown before commit."
          actions={<><Btn>Discard</Btn><Btn kind="p">Create dashboard package →</Btn></>}
        />
        <EsriIntake kind="Esri Dashboard JSON" file="q3-ops-dashboard.json · 142 KB"
          summary={['12 elements','3 selectors','1 header','2 unsupported widgets']} />

        <div style={{display:'grid', gridTemplateColumns:'1fr 380px', flex:1, overflow:'hidden'}}>
          <div style={{overflow:'auto'}}>
            <div style={{padding:'8px 18px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0}}>Element → widget mapping</h3>
              <div className="muted" style={{fontSize:11}}>Esri dashboard element → Honua dashboard widget</div>
            </div>
            <table className="tbl tbl--cmpt">
              <thead><tr>
                <th style={{width:24}}><input type="checkbox" readOnly defaultChecked /></th>
                <th>Esri element</th><th>Kind</th><th>→ Honua widget</th><th>Data</th><th>Fidelity</th><th>Note</th>
              </tr></thead>
              <tbody>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Header</b></td><td>header</td><td className="mono">title bar</td><td className="muted">—</td><td><Fid k="clean" /></td><td className="muted">title + logo</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Indicator · total</b></td><td>indicator</td><td className="mono">KPI</td><td className="mono">parcels_2024</td><td><Fid k="clean" /></td><td className="muted">sum mapped</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Serial chart</b></td><td>serialChart</td><td className="mono">Vega-Lite area</td><td className="mono">parcels_2024</td><td><Fid k="clean" /></td><td className="muted">time series</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Pie chart · use</b></td><td>pieChart</td><td className="mono">Vega-Lite arc</td><td className="mono">parcels_2024</td><td><Fid k="clean" /></td><td className="muted">categorical</td></tr>
                <tr style={{background:'#fff7e6'}}><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Gauge · capacity</b></td><td>gauge</td><td className="mono">KPI · radial</td><td className="mono">obs_stations</td><td><Fid k="degrade" /></td><td>gauge → radial KPI (no needle anim)</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>List · recent</b></td><td>list</td><td className="mono">table widget</td><td className="mono">fire_observations</td><td><Fid k="clean" /></td><td className="muted">—</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Map</b></td><td>mapWidget</td><td className="mono">embedded map</td><td><span style={{color:'var(--pencil)'}}>◇</span> parcels_2024</td><td><Fid k="clean" /></td><td className="muted">links existing map</td></tr>
                <tr style={{background:'#fbeae7'}}><td><input type="checkbox" readOnly /></td><td><b>Rich text · embed</b></td><td>richText (iframe)</td><td className="muted">—</td><td className="muted">—</td><td><Fid k="drop" /></td><td>arbitrary iframe blocked by policy</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Category selector</b></td><td>selector</td><td className="mono">filter chip</td><td className="mono">use_code</td><td><Fid k="clean" /></td><td className="muted">→ dashboard filter</td></tr>
              </tbody>
            </table>
          </div>

          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
              <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Target layout</div>
            </div>
            <div style={{padding:12}}>
              {/* faux dashboard layout */}
              <div style={{background:'#fff', border:'1px solid #d8d8d8', borderRadius:5, overflow:'hidden'}}>
                <div style={{padding:'6px 8px', background:'#fafafa', borderBottom:'1px solid #e4e4e4', fontSize:10, fontWeight:600}}>Q3 Operations</div>
                <div style={{padding:8, display:'grid', gridTemplateColumns:'1fr 1fr 1fr', gap:5}}>
                  {['KPI','KPI','radial'].map((t,i)=><div key={i} style={{height:36, background:'#f1f1f1', borderRadius:3, display:'grid', placeItems:'center', fontSize:9, color:'#888'}}>{t}</div>)}
                </div>
                <div style={{padding:'0 8px 8px', display:'grid', gridTemplateColumns:'1.4fr 1fr', gap:5}}>
                  <div style={{height:80, background:'#f1f1f1', borderRadius:3, display:'grid', placeItems:'center', fontSize:9, color:'#888'}}>area chart</div>
                  <div style={{height:80, background:'#f1f1f1', borderRadius:3, display:'grid', placeItems:'center', fontSize:9, color:'#888'}}>map</div>
                </div>
                <div style={{padding:'0 8px 8px'}}>
                  <div style={{height:50, background:'#f1f1f1', borderRadius:3, display:'grid', placeItems:'center', fontSize:9, color:'#888'}}>table</div>
                </div>
              </div>
              <FieldGroup title="Package" scope="resource">
                <FieldRow state="input" label="Name"><Inp mono value="q3-operations" /></FieldRow>
                <FieldRow state="calculated" label="Widgets in" value="8 of 9 (1 dropped)" derivedFrom="mapping" />
              </FieldGroup>
              <Callout kind="info">Layout approximated from Esri grid. Re-arrange in Studio after import.</Callout>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------- #104 · Import StoryMaps + Hub/Open Data sites ---------- */
function ImportStoryMap() {
  return (
    <div className="scr">
      <TopBar crumbs={['Operate','Import from Esri','StoryMap / Hub']} area="operate" />
      <Sidebar active="resources" />
      <div className="main">
        <PageHead
          title="Import StoryMap / Hub site"
          sub="Convert an ArcGIS StoryMap or Hub/Open-Data site into Console content (report + linked maps). P4 · lowest priority of the four."
          actions={<><Btn>Discard</Btn><Btn kind="p">Create content →</Btn></>}
        />
        <EsriIntake kind="ArcGIS StoryMap" file="https://storymaps.arcgis.com/stories/a3bf…"
          summary={['9 sections','4 embedded maps','12 media','3 sidecar blocks','1 swipe block']} />

        <div style={{display:'grid', gridTemplateColumns:'1fr 380px', flex:1, overflow:'hidden'}}>
          <div style={{overflow:'auto'}}>
            <div style={{padding:'8px 18px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0}}>Section → content mapping</h3>
              <div className="muted" style={{fontSize:11}}>StoryMap section → Console report block</div>
            </div>
            <table className="tbl tbl--cmpt">
              <thead><tr>
                <th style={{width:24}}><input type="checkbox" readOnly defaultChecked /></th>
                <th>Section</th><th>StoryMap block</th><th>→ Console block</th><th>Fidelity</th><th>Note</th>
              </tr></thead>
              <tbody>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Cover</b></td><td>cover</td><td className="mono">report hero</td><td><Fid k="clean" /></td><td className="muted">title + image</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Intro</b></td><td>text</td><td className="mono">markdown</td><td><Fid k="clean" /></td><td className="muted">—</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Map: parcels</b></td><td>map</td><td className="mono">embedded map</td><td><Fid k="clean" /></td><td className="muted">→ map package</td></tr>
                <tr style={{background:'#fff7e6'}}><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Sidecar: trends</b></td><td>sidecar</td><td className="mono">scroll section</td><td><Fid k="degrade" /></td><td>scroll-driven → stepped sections</td></tr>
                <tr style={{background:'#fff7e6'}}><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Swipe: before/after</b></td><td>swipe</td><td className="mono">side-by-side maps</td><td><Fid k="degrade" /></td><td>swipe → static comparison</td></tr>
                <tr><td><input type="checkbox" readOnly defaultChecked /></td><td><b>Media gallery</b></td><td>gallery</td><td className="mono">image grid</td><td><Fid k="clean" /></td><td className="muted">12 images</td></tr>
                <tr style={{background:'#fbeae7'}}><td><input type="checkbox" readOnly /></td><td><b>Embedded video</b></td><td>embed (YouTube)</td><td className="muted">—</td><td><Fid k="drop" /></td><td>external embed blocked · keep link</td></tr>
              </tbody>
            </table>
          </div>

          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
              <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Target content preview</div>
            </div>
            <div style={{padding:12}}>
              <div style={{background:'#fff', border:'1px solid #d8d8d8', borderRadius:5, overflow:'hidden'}}>
                <div style={{height:60, background:'#d9a23a', position:'relative'}}>
                  <div style={{position:'absolute', inset:0, background:'repeating-linear-gradient(135deg, rgba(255,255,255,0.06) 0 8px, rgba(0,0,0,0.04) 8px 16px)'}}/>
                  <div style={{position:'absolute', bottom:6, left:8, color:'#fff', fontWeight:700, fontSize:12}}>Watershed Health 2024</div>
                </div>
                <div style={{padding:'8px 10px', fontSize:10.5, color:'#444', lineHeight:1.5}}>Intro paragraph converted from StoryMap text block…</div>
                <div style={{margin:'0 10px 8px', height:70, background:'#f1f1f1', borderRadius:3, display:'grid', placeItems:'center', fontSize:9, color:'#888'}}>embedded map</div>
                <div style={{margin:'0 10px 8px', display:'grid', gridTemplateColumns:'1fr 1fr', gap:4}}>
                  <div style={{height:40, background:'#f1f1f1', borderRadius:3}}/>
                  <div style={{height:40, background:'#f1f1f1', borderRadius:3}}/>
                </div>
              </div>
              <FieldGroup title="Content" scope="resource">
                <FieldRow state="input" label="Type"><Sel value="Report" /></FieldRow>
                <FieldRow state="calculated" label="Blocks in" value="6 of 7 (1 dropped)" derivedFrom="mapping" />
                <FieldRow state="calculated" label="Linked maps" value="4 → map packages" derivedFrom="mapping" />
              </FieldGroup>
              <Callout kind="warn">Scroll-driven sidecar + swipe degrade to stepped/static. 1 external video dropped (link kept in notes).</Callout>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ImportEsriWebMap, ImportEsriDashboard, ImportStoryMap, Fid });
