// Honua Console · Share surface — public link, embed, open-data page, exports
// 4 screens:
//   ShareHome           — overview of everything shared from this workspace
//   ShareLinkConfig     — per-item: public link, embed code, expirations, access
//   ShareOpenDataPage   — opt-in public landing page editor (DCAT-ish)
//   ShareExports        — export targets (file, S3, FTP, scheduled)

function ShareHome() {
  return (
    <div className="scr">
      <TopBar crumbs={['Share']} area="share" />
      <Sidebar active="services" />
      <div className="main">
        <PageHead
          title="Share"
          sub="Everything this workspace publishes to the outside world — links, embeds, open-data pages, exports."
          actions={<>
            <Btn>Audit access</Btn>
            <Btn kind="p">+ New share</Btn>
          </>}
        />

        {/* KPI strip */}
        <div style={{padding:'10px 18px', display:'grid', gridTemplateColumns:'repeat(5, 1fr)', gap:8, borderBottom:'1px solid #e4e4e4'}}>
          {[
            ['Public links', '38', 'across 24 items'],
            ['Embed-enabled', '21', '15 unique origins'],
            ['Open-data pages', '6', 'public landing'],
            ['Scheduled exports', '4', '2 daily · 2 weekly'],
            ['Requests / 24h', '14.8k', 'on shared content'],
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
            <FiltChip on x>type: any</FiltChip>
            <FiltChip>visibility: any</FiltChip>
            <FiltChip>has embed</FiltChip>
            <FiltChip>expiring</FiltChip>
            <input className="inp" style={{width:200, height:22}} placeholder="Search shared items…" readOnly />
          </>}
          right={<span className="muted" style={{fontSize:11}}>24 shared items</span>}
        />

        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Item</th><th>Type</th><th>Visibility</th><th>Link</th><th>Embed</th><th>Open data</th><th>Requests / 24h</th><th>Expires</th><th></th>
            </tr></thead>
            <tbody>
              <tr>
                <td><span style={{color:'#666'}}>◐</span> <b>Parcels heatmap (FY24)</b></td>
                <td><span className="tag">map</span></td>
                <td><Badge kind="ok">public</Badge></td>
                <td className="mono" style={{fontSize:10}}>honua.example.gov/m/parcels-heatmap</td>
                <td><Badge kind="ok">any origin</Badge></td>
                <td><Badge>off</Badge></td>
                <td className="num mono">4,212</td>
                <td className="muted">no expiry</td>
                <td><Btn sm>Configure</Btn></td>
              </tr>
              <tr>
                <td><span style={{color:'#666'}}>▤</span> <b>Q3 land use review</b></td>
                <td><span className="tag">dashboard</span></td>
                <td><Badge>org read</Badge></td>
                <td className="mono" style={{fontSize:10}}>honua.example.gov/d/q3-land-use</td>
                <td><Badge>same-origin</Badge></td>
                <td><Badge>off</Badge></td>
                <td className="num mono">182</td>
                <td className="muted">no expiry</td>
                <td><Btn sm>Configure</Btn></td>
              </tr>
              <tr>
                <td><span style={{color:'#666'}}>◇</span> <b>parcels_2024</b></td>
                <td><span className="tag">data</span></td>
                <td><Badge kind="ok">public</Badge></td>
                <td className="mono" style={{fontSize:10}}>honua.example.gov/data/parcels_2024</td>
                <td><Badge>n/a</Badge></td>
                <td><Badge kind="ok">live</Badge></td>
                <td className="num mono">6,401</td>
                <td className="muted">no expiry</td>
                <td><Btn sm>Configure</Btn></td>
              </tr>
              <tr>
                <td><span style={{color:'#666'}}>❒</span> <b>Wetlands inspector app</b></td>
                <td><span className="tag">app</span></td>
                <td><Badge kind="ok">public</Badge></td>
                <td className="mono" style={{fontSize:10}}>honua.example.gov/a/wetlands-inspector</td>
                <td><Badge kind="ok">listed origins</Badge></td>
                <td><Badge>off</Badge></td>
                <td className="num mono">2,118</td>
                <td className="muted">no expiry</td>
                <td><Btn sm>Configure</Btn></td>
              </tr>
              <tr>
                <td><span style={{color:'#666'}}>⊟</span> <b>Public works overview</b></td>
                <td><span className="tag">report</span></td>
                <td><Badge>org read</Badge></td>
                <td className="mono" style={{fontSize:10}}>honua.example.gov/r/pw-overview</td>
                <td><Badge>off</Badge></td>
                <td><Badge>off</Badge></td>
                <td className="num mono">42</td>
                <td className="muted">no expiry</td>
                <td><Btn sm>Configure</Btn></td>
              </tr>
              <tr style={{background:'#fff7e6'}}>
                <td><span style={{color:'#666'}}>◐</span> <b>Fire perimeter map (Q3)</b></td>
                <td><span className="tag">map</span></td>
                <td><Badge>token link</Badge></td>
                <td className="mono" style={{fontSize:10}}>…/m/fire-q3?token=…</td>
                <td><Badge>same-origin</Badge></td>
                <td><Badge>off</Badge></td>
                <td className="num mono">312</td>
                <td><Badge kind="warn">in 6d</Badge></td>
                <td><Btn sm>Renew</Btn></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function ShareLinkConfig() {
  return (
    <div className="scr">
      <TopBar crumbs={['Share','Parcels heatmap (FY24)']} area="share" />
      <Sidebar active="services" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Share <span style={{color:'#bbb'}}>/</span> Maps <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <span style={{fontSize:18, color:'#666'}}>◐</span>
            <h1 style={{margin:0, font:'600 18px var(--ui)'}}>Parcels heatmap (FY24)</h1>
            <Badge kind="ok" lg>public</Badge>
            <Badge kind="ok">embed enabled</Badge>
            <span className="muted" style={{fontSize:11}}>4,212 requests last 24h · indexed by search engines</span>
            <div style={{flex:1}}/>
            <Btn ghost>Preview public ↗</Btn>
            <Btn ghost>Audit</Btn>
            <Btn kind="p">Save changes</Btn>
          </div>
        </div>

        <Tabs items={[
          { k:'link',     t:'Public link' },
          { k:'embed',    t:'Embed' },
          { k:'opendata', t:'Open-data page' },
          { k:'exports',  t:'Exports' },
          { k:'audit',    t:'Audit & traffic' },
        ]} active="link" />

        <div style={{display:'grid', gridTemplateColumns:'1fr 320px', flex:1, overflow:'hidden'}}>
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            {/* VISIBILITY */}
            <FieldGroup title="Visibility" sub="who can reach this map" scope="publication">
              <FieldRow state="input" label="Audience">
                <div className="col" style={{gap:4, fontSize:11}}>
                  <label className="row" style={{gap:6}}><input type="radio" readOnly defaultChecked /> Public · anyone with the link, indexed</label>
                  <label className="row" style={{gap:6}}><input type="radio" readOnly /> Organisation · signed-in workspace members</label>
                  <label className="row" style={{gap:6}}><input type="radio" readOnly /> Token link · expiring URL with token</label>
                  <label className="row" style={{gap:6}}><input type="radio" readOnly /> Private · only you and admins</label>
                </div>
              </FieldRow>
              <FieldRow state="discovered" label="Public URL" hint="derived from content title">
                <div style={{display:'flex', alignItems:'center', gap:6, width:'100%'}}>
                  <code className="mono" style={{flex:1, background:'#fafafa', border:'1px solid #e4e4e4', padding:'3px 8px', borderRadius:3, fontSize:11}}>
                    https://honua.example.gov/m/parcels-heatmap
                  </code>
                  <Btn ghost sm>⧉ Copy</Btn>
                  <Btn ghost sm>Open ↗</Btn>
                </div>
              </FieldRow>
              <FieldRow state="input" label="Custom slug" hint="overrides derived URL"><Inp mono value="(use derived)" /></FieldRow>
              <FieldRow state="input" label="Search engine indexing">
                <label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked /> include in robots.txt + sitemap</label>
              </FieldRow>
            </FieldGroup>

            {/* EMBED */}
            <FieldGroup title="Embed" sub="for external pages and reports" scope="publication">
              <FieldRow state="input" label="Embeddable">
                <label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked /> allow iframe embedding</label>
              </FieldRow>
              <FieldRow state="input" label="Allowed origins" hint="* = any origin">
                <div style={{display:'flex', flexWrap:'wrap', gap:4}}>
                  <span className="tag" style={{background:'#fff'}}>*</span>
                  <Btn ghost sm>+ Origin</Btn>
                </div>
              </FieldRow>
              <FieldRow state="discovered" label="Embed snippet" hint="HTML + JS · auto-generated">
                <pre className="mono" style={{margin:0, width:'100%', padding:8, background:'#0e0e0e', color:'#d8d8d8', borderRadius:4, fontSize:10, whiteSpace:'pre-wrap'}}>
{`<iframe src="https://honua.example.gov/m/parcels-heatmap?embed=1"
  width="100%" height="480" frameborder="0"
  allow="geolocation"
  referrerpolicy="origin-when-cross-origin"></iframe>`}
                </pre>
              </FieldRow>
              <FieldRow state="input" label="Embed options">
                <div className="col" style={{gap:3, fontSize:11}}>
                  <label className="row" style={{gap:6}}><input type="checkbox" readOnly defaultChecked /> show controls</label>
                  <label className="row" style={{gap:6}}><input type="checkbox" readOnly defaultChecked /> show legend</label>
                  <label className="row" style={{gap:6}}><input type="checkbox" readOnly /> hide branding</label>
                  <label className="row" style={{gap:6}}><input type="checkbox" readOnly defaultChecked /> show "powered by Honua" footer</label>
                </div>
              </FieldRow>
            </FieldGroup>

            {/* OPEN DATA */}
            <FieldGroup title="Open-data page" sub="public landing with download links" scope="publication">
              <FieldRow state="input" label="Enable public landing">
                <label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly /> create open-data page · off</label>
              </FieldRow>
              <Callout kind="info">When enabled, generates a public page with description, license, contact, download links (GeoJSON, CSV, PNG, PDF), DCAT metadata.</Callout>
            </FieldGroup>

            {/* EXPIRATIONS */}
            <FieldGroup title="Token links · expirations" sub="for ad-hoc sharing">
              <FieldRow state="input" label="Issue token link">
                <Btn sm>+ Generate token link</Btn>
              </FieldRow>
              <FieldRow state="system" label="Active tokens" value="0 active · 2 expired" />
            </FieldGroup>
          </div>

          {/* SIDE */}
          <div style={{borderLeft:'1px solid #e4e4e4', padding:'14px 14px', overflow:'auto', background:'#fafafa'}}>
            <div className="card">
              <h3>Preview</h3>
              <MapPreview mode="layer" height={200} popup={false} scaleText="1:8,000" />
              <div className="muted" style={{fontSize:10.5, marginTop:4}}>as public would see it · owner_name redacted</div>
            </div>

            <Callout kind="info">
              <b>Share is a governed surface.</b> Every change emits an audit event. Token links log the issuer + reason.
            </Callout>

            <div className="card">
              <h3>Traffic · last 7d</h3>
              <div className="col" style={{gap:4, fontSize:11}}>
                <div className="row"><span style={{flex:1}}>Page views</span><span className="mono">28,420</span></div>
                <div className="row"><span style={{flex:1}}>Unique visitors</span><span className="mono">4,118</span></div>
                <div className="row"><span style={{flex:1}}>Embed loads</span><span className="mono">12,084</span></div>
                <div className="row"><span style={{flex:1}}>Top referrer</span><span className="mono" style={{fontSize:10}}>maps.partner.gov</span></div>
              </div>
            </div>

            <Ann red>changes here propagate immediately. uncheck "search engine indexing" to deindex on next crawl.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function ShareOpenDataPage() {
  return (
    <div className="scr">
      <TopBar crumbs={['Share','parcels_2024','Open-data page']} area="share" />
      <Sidebar active="services" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="muted" style={{fontSize:11}}>Share <span style={{color:'#bbb'}}>/</span> Data resources <span style={{color:'#bbb'}}>/</span></div>
          <div className="row">
            <span style={{color:'var(--pencil)', fontSize:18}}>◇</span>
            <h1 style={{margin:0, font:'600 18px var(--ui)'}}>parcels_2024 · open-data page</h1>
            <Badge kind="ok" lg>live</Badge>
            <span className="muted" style={{fontSize:11}}>/data/parcels_2024 · 6,401 requests in 24h</span>
            <div style={{flex:1}}/>
            <Btn ghost>Preview ↗</Btn>
            <Btn ghost>Take offline</Btn>
            <Btn kind="p">Save</Btn>
          </div>
        </div>

        <Tabs items={[
          { k:'link',     t:'Public link' },
          { k:'embed',    t:'Embed' },
          { k:'opendata', t:'Open-data page' },
          { k:'exports',  t:'Exports', ct: 3 },
          { k:'audit',    t:'Audit & traffic' },
        ]} active="opendata" />

        <div style={{display:'grid', gridTemplateColumns:'1fr 380px', flex:1, overflow:'hidden'}}>
          {/* Editor */}
          <div style={{overflow:'auto', padding:'14px 18px'}}>
            <FieldGroup title="Page identity" sub="from canonical metadata" scope="resource" count="3 layers reuse this">
              <FieldRow state="calculated" label="Title" value="Tax Parcels (FY 2024)" derivedFrom="Resource.Metadata.Title" />
              <FieldRow state="calculated" label="Slug" value="parcels-2024" derivedFrom="Resource.Metadata.Name" />
              <FieldRow state="calculated" label="Description" value="Statewide tax parcel boundaries for fiscal year 2024…" derivedFrom="Resource.Metadata.Description" />
              <FieldRow state="input" label="Page hero">
                <div className="row" style={{gap:6}}>
                  <div style={{width:80, height:54, background:'#d9a23a', border:'1px solid #999', borderRadius:3}}/>
                  <Btn ghost sm>Upload…</Btn>
                  <Btn ghost sm>Use map preview</Btn>
                </div>
              </FieldRow>
              <FieldRow state="input" label="Page intro" hint="markdown · 1–2 paragraphs">
                <textarea readOnly className="inp" style={{height:50, padding:6, resize:'none'}} defaultValue="Statewide parcels, refreshed nightly. Free to use under CC-BY 4.0 with attribution." />
              </FieldRow>
            </FieldGroup>

            <FieldGroup title="Downloads" sub="formats consumers can grab" scope="publication">
              <FieldRow state="input" label="Available formats">
                <div className="col" style={{gap:3, fontSize:11}}>
                  {[
                    { k:'GeoJSON', d:'whole dataset · 248 MB', on:true },
                    { k:'GeoPackage', d:'OGC standard · 184 MB', on:true },
                    { k:'Shapefile (zip)', d:'legacy · 218 MB', on:true },
                    { k:'CSV (WKT)', d:'tabular · 142 MB', on:true },
                    { k:'PMTiles', d:'web map tiles · 92 MB', on:true },
                    { k:'KML/KMZ', d:'Google Earth · 86 MB', on:false },
                  ].map(f => (
                    <label key={f.k} className="row" style={{gap:6, padding:'4px 8px', border:'1px solid #e4e4e4', borderRadius:4, background:'#fff'}}>
                      <input type="checkbox" readOnly defaultChecked={f.on} />
                      <span style={{flex:1, fontWeight:600}}>{f.k}</span>
                      <span className="muted" style={{fontSize:10.5}}>{f.d}</span>
                    </label>
                  ))}
                </div>
              </FieldRow>
              <FieldRow state="input" label="Generate strategy">
                <Sel value="On-demand · cache for 7 days" />
              </FieldRow>
              <FieldRow state="discovered" label="Last regenerated" value="2h ago · 248 MB" />
            </FieldGroup>

            <FieldGroup title="License & contact" sub="from canonical metadata · editable here" scope="resource">
              <FieldRow state="calculated" label="License" value="CC-BY 4.0" derivedFrom="Resource.Metadata.License" />
              <FieldRow state="calculated" label="Attribution text" value="© State Assessor · Updated nightly by Honua" derivedFrom="Resource.Metadata.Rights" />
              <FieldRow state="calculated" label="Contact" value="data@assessor.ca.gov" derivedFrom="Resource.Metadata.Contact" />
            </FieldGroup>

            <FieldGroup title="Discoverability" scope="server">
              <FieldRow state="input" label="Tags">
                <div className="row" style={{flexWrap:'wrap', gap:4}}>
                  {['parcels','cadastre','assessor','FY2024','open-data','california'].map(t => <span key={t} className="tag">#{t} ×</span>)}
                  <Btn ghost sm>+ Tag</Btn>
                </div>
              </FieldRow>
              <FieldRow state="input" label="DCAT publishing">
                <label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked /> register in DCAT catalog</label>
              </FieldRow>
              <FieldRow state="input" label="schema.org/Dataset markup">
                <label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked /> embed JSON-LD · helps Google Dataset Search</label>
              </FieldRow>
            </FieldGroup>
          </div>

          {/* Live page preview */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
              <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Live preview</div>
            </div>
            <div style={{margin:'12px', background:'#fff', border:'1px solid #d8d8d8', borderRadius:6, overflow:'hidden'}}>
              <div style={{padding:'14px 16px', borderBottom:'1px solid #e4e4e4'}}>
                <div className="muted" style={{fontSize:10}}>honua.example.gov / data / parcels_2024</div>
                <div style={{font:'700 16px var(--ui)', marginTop:4}}>Tax Parcels (FY 2024)</div>
                <div style={{marginTop:4, display:'flex', gap:4, flexWrap:'wrap'}}>
                  {['parcels','cadastre','assessor','FY2024','california'].map(t => <span key={t} className="tag">#{t}</span>)}
                </div>
              </div>
              <div style={{padding:0}}>
                <div style={{width:'100%', height:140, background:'#d9a23a', position:'relative'}}>
                  <div style={{position:'absolute', inset:0, background:'repeating-linear-gradient(135deg, rgba(255,255,255,0.06) 0 8px, rgba(0,0,0,0.04) 8px 16px)'}}/>
                </div>
              </div>
              <div style={{padding:'12px 16px', fontSize:11.5, lineHeight:1.55}}>
                Statewide parcels, refreshed nightly. Free to use under CC-BY 4.0 with attribution.
              </div>
              <div style={{padding:'10px 16px', borderTop:'1px solid #e4e4e4', background:'#fafafa'}}>
                <div className="muted" style={{fontSize:9.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:6}}>Download</div>
                <div className="col" style={{gap:4}}>
                  {[['GeoJSON','248 MB'],['GeoPackage','184 MB'],['Shapefile','218 MB'],['CSV (WKT)','142 MB'],['PMTiles','92 MB']].map(([k,s]) => (
                    <div key={k} className="row" style={{padding:'4px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:3, fontSize:11}}>
                      <span style={{flex:1, fontWeight:600}}>{k}</span>
                      <span className="muted" style={{fontSize:10}}>{s}</span>
                      <span style={{color:'var(--pencil)', fontSize:11}}>↓</span>
                    </div>
                  ))}
                </div>
              </div>
              <div style={{padding:'10px 16px', borderTop:'1px solid #e4e4e4', fontSize:10.5, color:'#555'}}>
                License: CC-BY 4.0 · © State Assessor<br/>
                Contact: data@assessor.ca.gov<br/>
                Updated: 2h ago · nightly refresh
              </div>
              <div style={{padding:'6px 16px', borderTop:'1px solid #e4e4e4', background:'#fafafa', fontSize:9.5, color:'#888', textAlign:'center'}}>
                powered by Honua · public open-data
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ShareExports() {
  return (
    <div className="scr">
      <TopBar crumbs={['Share','parcels_2024','Exports']} area="share" />
      <Sidebar active="services" />
      <div className="main">
        <div style={{padding:'12px 18px 0'}}>
          <div className="row">
            <span style={{color:'var(--pencil)', fontSize:18}}>◇</span>
            <h1 style={{margin:0, font:'600 18px var(--ui)'}}>parcels_2024 · scheduled exports</h1>
            <Badge kind="ok" lg>4 active</Badge>
            <div style={{flex:1}}/>
            <Btn ghost>Run now</Btn>
            <Btn kind="p">+ New export</Btn>
          </div>
        </div>

        <Tabs items={[
          { k:'link',     t:'Public link' },
          { k:'embed',    t:'Embed' },
          { k:'opendata', t:'Open-data page' },
          { k:'exports',  t:'Exports', ct: 4 },
          { k:'audit',    t:'Audit & traffic' },
        ]} active="exports" />

        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt">
            <thead><tr>
              <th>Name</th><th>Target</th><th>Format</th><th>Filter</th><th>Schedule</th><th>Last run</th><th>State</th><th></th>
            </tr></thead>
            <tbody>
              <tr>
                <td><b>BI nightly · all parcels</b></td>
                <td className="mono">s3://honua-bi/parcels/</td>
                <td>GeoJSON.gz</td>
                <td className="muted">all features</td>
                <td className="mono">daily · 02:00 UTC</td>
                <td className="muted">2m ago</td>
                <td><Badge kind="ok">success</Badge></td>
                <td><Btn ghost sm>Edit</Btn></td>
              </tr>
              <tr>
                <td><b>Assessor weekly</b></td>
                <td className="mono">sftp://assessor.ca.gov/incoming</td>
                <td>Shapefile</td>
                <td>use_code IN ('R-1','R-2')</td>
                <td className="mono">weekly · Mon 06:00 UTC</td>
                <td className="muted">3d ago</td>
                <td><Badge kind="ok">success</Badge></td>
                <td><Btn ghost sm>Edit</Btn></td>
              </tr>
              <tr>
                <td><b>Partner tiles</b></td>
                <td className="mono">https://maps.partner.gov/in</td>
                <td>PMTiles</td>
                <td className="muted">all features</td>
                <td className="mono">on publish</td>
                <td className="muted">2h ago</td>
                <td><Badge kind="ok">success</Badge></td>
                <td><Btn ghost sm>Edit</Btn></td>
              </tr>
              <tr style={{background:'#fff7e6'}}>
                <td><b>Audit · monthly snapshot</b></td>
                <td className="mono">s3://honua-audit/snapshots/</td>
                <td>GeoPackage</td>
                <td className="muted">all features + audit cols</td>
                <td className="mono">monthly · 1st 04:00 UTC</td>
                <td className="muted">2d ago</td>
                <td><Badge kind="warn">slow · 18m</Badge></td>
                <td><Btn ghost sm>Edit</Btn></td>
              </tr>
            </tbody>
          </table>
        </div>

        <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', background:'#fafafa', fontSize:11, color:'#666'}}>
          <span className="muted">Exports run as scheduled jobs · check Event viewer for history. Token rotations and connection failures surface as alerts.</span>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ShareHome, ShareLinkConfig, ShareOpenDataPage, ShareExports });
