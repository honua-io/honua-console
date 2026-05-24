// Studio · Form + Workflow content types
//   StudioFormAI         — prompt + form structure preview
//   StudioFormBuilder    — field designer + logic + map binding + offline rules
//   StudioWorkflowAI     — prompt + DAG preview
//   StudioWorkflowEditor — visual pipeline editor

function StudioFormAI() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Form','New from prompt']} area="studio" />
      <StudioSidebar active="form" />
      <div className="main" style={{display:'grid', gridTemplateColumns:'380px 1fr', overflow:'hidden'}}>
        <div style={{borderRight:'1px solid #e4e4e4', display:'flex', flexDirection:'column', background:'#fafafa'}}>
          <div style={{padding:'10px 14px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
            <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Conversation</div>
            <div style={{font:'600 12px var(--ui)'}}>Form from prompt</div>
          </div>
          <div style={{flex:1, overflow:'auto', padding:'12px 14px', display:'flex', flexDirection:'column', gap:12}}>
            <div>
              <div className="muted" style={{fontSize:10}}>You</div>
              <div style={{padding:'8px 10px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:5, marginTop:2, fontSize:11.5}}>
                Hydrant inspection form — location, photo, condition rating, parts needed, notes. Field crews use it offline on tablets.
              </div>
            </div>
            <div>
              <div className="muted" style={{fontSize:10}}>Honua</div>
              <div style={{padding:'8px 10px', background:'#fffae0', border:'1.2px solid var(--accent-deep)', borderRadius:5, marginTop:2, fontSize:11.5}}>
                <div style={{marginBottom:4}}>Two clarifications:</div>
                <div className="col" style={{gap:6}}>
                  <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4}}>
                    <div style={{fontSize:11, fontWeight:600}}>1. Bind to existing hydrants?</div>
                    <div className="muted" style={{fontSize:10.5, marginBottom:4}}>I see <span className="mono">hydrants_2024</span> (14k point features). Use as the lookup so each submission updates a hydrant?</div>
                    <div className="row" style={{gap:4, fontSize:10}}>
                      <label className="row" style={{gap:3}}><input type="radio" readOnly defaultChecked /> bind · updates existing</label>
                      <label className="row" style={{gap:3}}><input type="radio" readOnly /> standalone · new collection</label>
                    </div>
                  </div>
                  <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4}}>
                    <div style={{fontSize:11, fontWeight:600}}>2. Offline mode?</div>
                    <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Submissions stored locally and sync when reconnected. PII handling on photo metadata?</div>
                    <div className="row" style={{gap:4, fontSize:10}}>
                      <label className="row" style={{gap:3}}><input type="checkbox" readOnly defaultChecked /> offline-capable</label>
                      <label className="row" style={{gap:3}}><input type="checkbox" readOnly defaultChecked /> strip EXIF GPS from photos before upload</label>
                    </div>
                  </div>
                </div>
              </div>
            </div>
            <div>
              <div className="muted" style={{fontSize:10}}>You</div>
              <div style={{padding:'8px 10px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:5, marginTop:2, fontSize:11.5}}>
                Bind to hydrants. Yes offline. Strip EXIF.
              </div>
            </div>
            <div>
              <div className="muted" style={{fontSize:10}}>Honua</div>
              <div style={{padding:'8px 10px', background:'#ecf7f0', border:'1px solid #8fcfa6', borderRadius:5, marginTop:2, fontSize:11.5}}>
                Form package ready · 8 fields · 2 logic rules · offline + EXIF strip enabled.
              </div>
            </div>
          </div>
          <div style={{padding:'8px 12px', borderTop:'1px solid #e4e4e4', background:'#fff'}}>
            <textarea readOnly className="inp" style={{height:40, padding:8, fontSize:11.5, resize:'none'}} placeholder="Refine…" />
            <div className="row" style={{marginTop:6}}><Btn kind="p" sm>Send ⌘↵</Btn></div>
          </div>
        </div>

        <div style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
          <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
            <span style={{fontSize:16}}>☱</span>
            <input readOnly className="inp" style={{width:240, fontWeight:600}} value="hydrant-inspection" />
            <Badge>draft</Badge>
            <Badge kind="ok">offline-ready</Badge>
            <div style={{flex:1}}/>
            <Btn>Open builder →</Btn>
            <Btn kind="p">Publish…</Btn>
          </div>

          <div style={{display:'grid', gridTemplateColumns:'320px 1fr', flex:1, overflow:'hidden'}}>
            <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
                <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Form package</div>
                <div style={{font:'600 11.5px var(--ui)'}}>hydrant-inspection · v1 draft</div>
              </div>
              <div style={{padding:'10px 12px'}}>
                <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Bound resource</div>
                <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, marginBottom:10, fontSize:11}}>
                  <div className="row"><span style={{color:'var(--pencil)'}}>◇</span><span style={{flex:1, fontWeight:600, marginLeft:4}}>hydrants_2024</span><Badge kind="ok">bound</Badge></div>
                  <div className="muted" style={{fontSize:10}}>14k Point features · updates on submit</div>
                </div>

                <div className="muted" style={{fontSize:10.5, marginBottom:4}}>Fields · 8</div>
                {[
                  ['📍','hydrant_id','lookup · select from map'],
                  ['📷','photo','required · EXIF stripped'],
                  ['⚪','condition','radio · Good / Fair / Poor / Out of service'],
                  ['☑','parts_needed','multi · 12 parts'],
                  ['📝','notes','text · optional'],
                  ['⏱','inspected_at','auto · timestamp'],
                  ['👤','inspector','auto · from session'],
                  ['📡','gps_accuracy','auto · device sensor'],
                ].map((f,i) => (
                  <div key={i} style={{padding:'4px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, marginBottom:3, fontSize:11}}>
                    <div className="row">
                      <span style={{width:14, textAlign:'center'}}>{f[0]}</span>
                      <span className="mono" style={{fontSize:10.5, fontWeight:600, flex:1}}>{f[1]}</span>
                    </div>
                    <div className="muted" style={{fontSize:10, marginLeft:18}}>{f[2]}</div>
                  </div>
                ))}

                <div className="muted" style={{fontSize:10.5, marginTop:10, marginBottom:4}}>Logic · 2 rules</div>
                <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, marginBottom:3, fontSize:11}}>
                  <div className="mono" style={{fontSize:10}}>if condition = Poor → require parts_needed</div>
                </div>
                <div style={{padding:'6px 8px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:4, fontSize:11}}>
                  <div className="mono" style={{fontSize:10}}>if condition = Out of service → notify supervisor</div>
                </div>

                <div className="muted" style={{fontSize:10.5, marginTop:10, marginBottom:4}}>Offline rules</div>
                <div style={{padding:'6px 8px', background:'#ecf7f0', border:'1px solid #8fcfa6', borderRadius:4, fontSize:10.5}}>
                  ✓ submissions cached locally<br/>✓ photos compressed before sync<br/>✓ EXIF GPS stripped at capture<br/>✓ conflict policy: latest-wins per hydrant
                </div>

                <div className="muted" style={{fontSize:10.5, marginTop:10, marginBottom:4}}>Validation</div>
                <div style={{padding:'6px 8px', background:'#ecf7f0', border:'1px solid #8fcfa6', borderRadius:4, fontSize:10.5}}>
                  ✓ resource binding valid · ✓ schema matches · ✓ logic graph acyclic
                </div>
              </div>
            </div>

            {/* Preview as tablet */}
            <div style={{padding:20, background:'#fafafa', display:'flex', justifyContent:'center', overflow:'auto'}}>
              <div style={{
                width:300, background:'#fff', border:'8px solid #141414', borderRadius:20, padding:0, overflow:'hidden',
                boxShadow:'0 12px 32px rgba(0,0,0,.15)'
              }}>
                <div style={{padding:'10px 14px', background:'#fafafa', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center'}}>
                  <span style={{fontSize:14, marginRight:8}}>☱</span>
                  <div style={{flex:1, font:'600 12px var(--ui)'}}>Hydrant inspection</div>
                  <span className="tag" style={{fontSize:9}}>offline ✓</span>
                </div>
                <div style={{padding:'12px 14px', fontSize:11.5, display:'flex', flexDirection:'column', gap:10}}>
                  <div>
                    <div className="muted" style={{fontSize:10}}>Hydrant</div>
                    <div style={{padding:'8px 10px', border:'1px dashed #c5c5c5', borderRadius:4, fontFamily:'var(--mono)', fontSize:11, color:'#888'}}>
                      📍 tap to select on map
                    </div>
                  </div>
                  <div>
                    <div className="muted" style={{fontSize:10}}>Photo · required</div>
                    <div style={{height:60, border:'1px dashed #c5c5c5', borderRadius:4, display:'grid', placeItems:'center', fontSize:11, color:'#888'}}>📷 tap to capture</div>
                  </div>
                  <div>
                    <div className="muted" style={{fontSize:10, marginBottom:3}}>Condition</div>
                    <div className="col" style={{gap:3}}>
                      {['Good','Fair','Poor','Out of service'].map(c => (
                        <label key={c} className="row" style={{gap:6, fontSize:11, padding:'3px 6px', border:'1px solid #e4e4e4', borderRadius:3}}>
                          <input type="radio" readOnly /><span>{c}</span>
                        </label>
                      ))}
                    </div>
                  </div>
                  <div className="muted" style={{fontSize:10, color:'#999'}}>+ 5 more fields…</div>
                </div>
                <div style={{padding:'10px 14px', borderTop:'1px solid #e4e4e4', display:'flex', gap:6}}>
                  <button style={{flex:1, padding:'6px 10px', background:'#fff', border:'1px solid #d8d8d8', borderRadius:4, fontSize:11}}>Save draft</button>
                  <button style={{flex:1, padding:'6px 10px', background:'#141414', border:'1px solid #141414', color:'#fff', borderRadius:4, fontSize:11}}>Submit</button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StudioFormBuilder() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Form','hydrant-inspection','Build']} area="studio" />
      <StudioSidebar active="form" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>☱</span>
          <input readOnly className="inp" style={{width:240, fontWeight:600}} value="hydrant-inspection" />
          <Badge>draft v1</Badge>
          <Badge kind="ok">offline-ready</Badge>
          <div style={{flex:1}}/>
          <Btn ghost>Save draft</Btn>
          <Btn kind="p">Publish…</Btn>
        </div>

        <Tabs items={[
          { k:'fields',  t:'Fields', ct: 8 },
          { k:'logic',   t:'Logic', ct: 2 },
          { k:'binding', t:'Resource binding' },
          { k:'offline', t:'Offline' },
          { k:'access',  t:'Access' },
        ]} active="fields" />

        <div style={{display:'grid', gridTemplateColumns:'260px 1fr 280px', flex:1, overflow:'hidden'}}>
          {/* field list */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Fields</h3>
              <div style={{flex:1}}/>
              <Btn ghost sm>+ Field</Btn>
            </div>
            {[
              { i:'📍', n:'hydrant_id', t:'lookup' },
              { i:'📷', n:'photo', t:'image', sel:true },
              { i:'⚪', n:'condition', t:'radio' },
              { i:'☑', n:'parts_needed', t:'multi' },
              { i:'📝', n:'notes', t:'text' },
              { i:'⏱', n:'inspected_at', t:'auto' },
              { i:'👤', n:'inspector', t:'auto' },
              { i:'📡', n:'gps_accuracy', t:'auto' },
            ].map(f => (
              <div key={f.n} className="row" style={{
                padding:'5px 12px', borderBottom:'1px solid #f1f1f1',
                background: f.sel ? '#fffae0' : 'transparent',
                borderLeft: f.sel ? '3px solid var(--ink)' : '3px solid transparent',
                cursor:'pointer', fontSize:11
              }}>
                <span style={{width:14, textAlign:'center'}}>{f.i}</span>
                <span className="mono" style={{flex:1, fontWeight: f.sel ? 600 : 400}}>{f.n}</span>
                <span className="muted" style={{fontSize:9.5}}>{f.t}</span>
              </div>
            ))}
          </div>

          {/* canvas */}
          <div style={{padding:14, overflow:'auto', display:'flex', justifyContent:'center', background:'#fafafa'}}>
            <div style={{width:360, background:'#fff', border:'1px solid #d8d8d8', borderRadius:8, padding:14}}>
              <div className="muted" style={{fontSize:10, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:8}}>preview · tablet</div>
              <div style={{font:'600 14px var(--ui)', marginBottom:10}}>Hydrant inspection</div>
              <div className="col" style={{gap:12}}>
                <div>
                  <div className="muted" style={{fontSize:10.5, marginBottom:3}}>Hydrant</div>
                  <div style={{padding:'8px 10px', border:'1px solid #e4e4e4', borderRadius:4, fontSize:11, color:'#444'}}>📍 H-04-021 · 39.2N 121.0W</div>
                </div>
                <div style={{border:'2px solid var(--ink)', borderRadius:6, padding:10, background:'#fffae0'}}>
                  <div className="muted" style={{fontSize:10.5, marginBottom:3}}>Photo · required · EXIF will be stripped</div>
                  <div style={{height:80, border:'1px dashed #c5c5c5', borderRadius:4, display:'grid', placeItems:'center', fontSize:11, color:'#888'}}>📷 tap to capture</div>
                  <div className="muted" style={{fontSize:10, marginTop:4}}>selected field · edit on right</div>
                </div>
                <div>
                  <div className="muted" style={{fontSize:10.5, marginBottom:3}}>Condition</div>
                  <div className="col" style={{gap:3}}>
                    {['Good','Fair','Poor','Out of service'].map(c => (
                      <label key={c} className="row" style={{gap:6, fontSize:11, padding:'4px 8px', border:'1px solid #e4e4e4', borderRadius:3}}>
                        <input type="radio" readOnly /><span>{c}</span>
                      </label>
                    ))}
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* inspector for "photo" field */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>photo</h3>
              <div className="muted" style={{fontSize:10}}>image field</div>
            </div>
            <div style={{padding:'10px 12px'}}>
              <FieldGroup title="Identity">
                <FieldRow state="input" label="Field name"><Inp mono value="photo" /></FieldRow>
                <FieldRow state="input" label="Label"><Inp value="Photo" /></FieldRow>
                <FieldRow state="input" label="Help text"><Inp value="Front-on, well-lit." /></FieldRow>
              </FieldGroup>
              <FieldGroup title="Validation">
                <FieldRow state="input" label="Required"><label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked /><span>yes</span></label></FieldRow>
                <FieldRow state="input" label="Max size"><Inp mono value="5 MB" /></FieldRow>
                <FieldRow state="input" label="Min count"><Inp mono value="1" /></FieldRow>
                <FieldRow state="input" label="Max count"><Inp mono value="3" /></FieldRow>
              </FieldGroup>
              <FieldGroup title="Privacy">
                <FieldRow state="input" label="Strip EXIF GPS"><label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked /><span>at capture time</span></label></FieldRow>
                <FieldRow state="input" label="Strip EXIF camera"><label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly /><span>keep for audit</span></label></FieldRow>
                <FieldRow state="input" label="Compress on cellular"><label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked /><span>2048px max</span></label></FieldRow>
              </FieldGroup>
              <FieldGroup title="Storage">
                <FieldRow state="calculated" label="Storage" value="workspace blob · public-works/photos/" derivedFrom="binding" />
                <FieldRow state="calculated" label="Naming" value="{hydrant_id}-{inspected_at}.jpg" derivedFrom="hydrant_id, inspected_at" />
              </FieldGroup>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StudioWorkflowAI() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Workflow','New from prompt']} area="studio" />
      <StudioSidebar active="workflow" />
      <div className="main" style={{display:'grid', gridTemplateColumns:'380px 1fr', overflow:'hidden'}}>
        <div style={{borderRight:'1px solid #e4e4e4', display:'flex', flexDirection:'column', background:'#fafafa'}}>
          <div style={{padding:'10px 14px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
            <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em'}}>Conversation</div>
            <div style={{font:'600 12px var(--ui)'}}>Workflow from prompt</div>
          </div>
          <div style={{flex:1, overflow:'auto', padding:'12px 14px', display:'flex', flexDirection:'column', gap:12}}>
            <div>
              <div className="muted" style={{fontSize:10}}>You</div>
              <div style={{padding:'8px 10px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:5, marginTop:2, fontSize:11.5}}>
                Nightly: pull assessor CSV from SFTP, validate against parcels_2024 schema, geocode missing coords, append new parcels, publish v+1 to FeatureServer + OGC API, notify if anything fails.
              </div>
            </div>
            <div>
              <div className="muted" style={{fontSize:10}}>Honua</div>
              <div style={{padding:'8px 10px', background:'#ecf7f0', border:'1px solid #8fcfa6', borderRadius:5, marginTop:2, fontSize:11.5}}>
                <div style={{marginBottom:4}}>Workflow ready · 7 steps · ~12 min estimated</div>
                <div className="muted" style={{fontSize:10.5}}>Trigger: cron daily 02:00 UTC · failure routes to <span className="mono">#gis-ops</span> · evidence retained 90d</div>
              </div>
            </div>
          </div>
          <div style={{padding:'8px 12px', borderTop:'1px solid #e4e4e4', background:'#fff'}}>
            <textarea readOnly className="inp" style={{height:40, padding:8, fontSize:11.5, resize:'none'}} placeholder="Refine…" />
            <div className="row" style={{marginTop:6}}><Btn kind="p" sm>Send ⌘↵</Btn></div>
          </div>
        </div>

        <div style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
          <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
            <span style={{fontSize:16}}>⇋</span>
            <input readOnly className="inp" style={{width:260, fontWeight:600}} value="parcels-nightly-refresh" />
            <Badge>draft</Badge>
            <Badge kind="ok">DAG valid · acyclic</Badge>
            <div style={{flex:1}}/>
            <Btn>Open editor →</Btn>
            <Btn ghost>Run once</Btn>
            <Btn kind="p">Publish…</Btn>
          </div>

          {/* DAG preview */}
          <div style={{flex:1, padding:'20px 24px', overflow:'auto', background:'#fafafa'}}>
            <div style={{position:'relative', minHeight:380}}>
              <svg width="100%" height="380" viewBox="0 0 1100 380" style={{position:'absolute', inset:0}}>
                <defs>
                  <marker id="wfa" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
                    <path d="M 0 0 L 10 5 L 0 10 z" fill="#141414" />
                  </marker>
                </defs>
                {/* edges main chain */}
                {[[110,180,240,180],[240,180,370,180],[370,180,500,180],[500,180,630,180],[630,180,760,140],[630,180,760,220],[760,140,890,180],[760,220,890,180]].map((e,i)=>(
                  <path key={i} d={`M ${e[0]} ${e[1]} L ${e[2]} ${e[3]}`} stroke="#141414" strokeWidth="1.4" fill="none" markerEnd="url(#wfa)" />
                ))}
                {/* failure edge to notify */}
                <path d="M 240 200 C 240 290, 700 320, 890 300" stroke="#c03b2b" strokeWidth="1.2" strokeDasharray="4 4" fill="none" markerEnd="url(#wfa)" />
                <text x="450" y="320" fontFamily="Inter" fontSize="10" fill="#c03b2b">on failure</text>
              </svg>

              {[
                { x: 30, y: 150, t:'⏱', name:'Trigger', sub:'cron · 02:00 UTC' },
                { x: 160, y: 150, t:'⤴', name:'Pull SFTP', sub:'assessor.csv · 248 MB' },
                { x: 290, y: 150, t:'✓', name:'Validate', sub:'schema · parcels_2024' },
                { x: 420, y: 150, t:'📍', name:'Geocode', sub:'missing coords · ~42 rows' },
                { x: 550, y: 150, t:'➕', name:'Append', sub:'+ new parcels · upsert by id' },
                { x: 680, y: 110, t:'🚀', name:'Publish FS', sub:'public-works-fs / 0' },
                { x: 680, y: 200, t:'🚀', name:'Publish OGC', sub:'features-public / parcels' },
                { x: 810, y: 150, t:'📢', name:'Notify', sub:'#gis-ops' },
                { x: 810, y: 280, t:'📢', name:'On failure', sub:'#gis-ops + pager', err:true },
              ].map((n,i) => (
                <div key={i} style={{
                  position:'absolute', left: n.x, top: n.y, width:110, padding:8,
                  background:'#fff', border: n.err ? '1.5px solid var(--bad)' : '1.5px solid #141414', borderRadius:6,
                  fontSize:11, textAlign:'center'
                }}>
                  <div style={{fontSize:14, marginBottom:2}}>{n.t}</div>
                  <div style={{fontWeight:600}}>{n.name}</div>
                  <div className="muted" style={{fontSize:9.5, marginTop:1}}>{n.sub}</div>
                </div>
              ))}
            </div>

            <div className="row" style={{marginTop:14, gap:6, flexWrap:'wrap'}}>
              <span className="muted" style={{fontSize:11}}>Step durations:</span>
              {[['Pull','42s'],['Validate','8s'],['Geocode','3m 20s'],['Append','1m 12s'],['Publish FS','45s'],['Publish OGC','38s'],['Notify','1s']].map(([n,d]) => (
                <span key={n} className="tag" style={{fontSize:10}}>{n} {d}</span>
              ))}
              <span className="muted" style={{fontSize:10, marginLeft:8}}>~ 6m 46s · estimated</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StudioWorkflowEditor() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Workflow','parcels-nightly-refresh','Edit']} area="studio" />
      <StudioSidebar active="workflow" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>⇋</span>
          <input readOnly className="inp" style={{width:260, fontWeight:600}} value="parcels-nightly-refresh" />
          <Badge>draft v1</Badge>
          <Badge kind="ok">DAG valid</Badge>
          <div style={{flex:1}}/>
          <Btn ghost>Run once</Btn>
          <Btn ghost>Save draft</Btn>
          <Btn kind="p">Publish…</Btn>
        </div>

        <Tabs items={[
          { k:'graph',    t:'Graph' },
          { k:'schedule', t:'Schedule' },
          { k:'secrets',  t:'Secrets' },
          { k:'history',  t:'Run history' },
          { k:'package',  t:'Package · raw' },
        ]} active="graph" />

        <div style={{display:'grid', gridTemplateColumns:'220px 1fr 320px', flex:1, overflow:'hidden'}}>
          {/* step palette */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Step palette</h3>
              <div className="muted" style={{fontSize:10}}>drag onto canvas</div>
            </div>
            {[
              ['⏱','Trigger','cron · webhook · event'],
              ['⤴','Pull','SFTP · S3 · HTTP'],
              ['📦','Read','Postgres · file · resource'],
              ['✓','Validate','schema · spatial · custom'],
              ['📍','Geocode','batch · service'],
              ['🔀','Transform','SQL · script · GP'],
              ['➕','Write','resource · file · SFTP'],
              ['🚀','Publish','service · catalog · share'],
              ['📢','Notify','Slack · email · webhook'],
              ['🤖','AI step','classify · summarise · clean'],
            ].map((s,i) => (
              <div key={i} className="row" style={{
                padding:'6px 12px', borderBottom:'1px solid #f1f1f1', cursor:'grab', fontSize:11
              }}>
                <span style={{width:14, textAlign:'center'}}>{s[0]}</span>
                <div style={{flex:1, minWidth:0}}>
                  <div style={{fontWeight:600}}>{s[1]}</div>
                  <div className="muted" style={{fontSize:9.5}}>{s[2]}</div>
                </div>
              </div>
            ))}
          </div>

          {/* canvas */}
          <div style={{position:'relative', overflow:'auto', background:'#fafafa', padding:20}}>
            <div style={{position:'relative', minHeight:340, width:1000}}>
              <svg width="1000" height="340" viewBox="0 0 1000 340" style={{position:'absolute', inset:0}}>
                <defs>
                  <marker id="wfe" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
                    <path d="M 0 0 L 10 5 L 0 10 z" fill="#141414" />
                  </marker>
                  <pattern id="dotgrid" width="20" height="20" patternUnits="userSpaceOnUse">
                    <circle cx="2" cy="2" r="1" fill="#ddd"/>
                  </pattern>
                </defs>
                <rect width="1000" height="340" fill="url(#dotgrid)"/>
                {[[110,160,240,160],[240,160,370,160],[370,160,500,160],[500,160,630,160],[630,160,760,120],[630,160,760,200],[760,120,890,160],[760,200,890,160]].map((e,i)=>(
                  <path key={i} d={`M ${e[0]} ${e[1]} L ${e[2]} ${e[3]}`} stroke="#141414" strokeWidth="1.6" fill="none" markerEnd="url(#wfe)" />
                ))}
                <path d="M 240 180 C 240 270, 700 300, 890 280" stroke="#c03b2b" strokeWidth="1.4" strokeDasharray="4 4" fill="none" markerEnd="url(#wfe)" />
              </svg>
              {[
                { x: 30, y: 130, t:'⏱', name:'Trigger', sub:'cron · 02:00 UTC' },
                { x: 160, y: 130, t:'⤴', name:'Pull SFTP', sub:'assessor.csv', sel:true },
                { x: 290, y: 130, t:'✓', name:'Validate', sub:'parcels_2024 schema' },
                { x: 420, y: 130, t:'📍', name:'Geocode', sub:'~42 rows' },
                { x: 550, y: 130, t:'➕', name:'Append', sub:'upsert by id' },
                { x: 680, y: 90, t:'🚀', name:'Publish FS', sub:'public-works-fs / 0' },
                { x: 680, y: 180, t:'🚀', name:'Publish OGC', sub:'features-public' },
                { x: 810, y: 130, t:'📢', name:'Notify', sub:'#gis-ops' },
                { x: 810, y: 260, t:'📢', name:'On failure', sub:'#gis-ops + pager', err:true },
              ].map((n,i) => (
                <div key={i} style={{
                  position:'absolute', left: n.x, top: n.y, width:110, padding:8,
                  background:'#fff',
                  border: n.sel ? '2px solid var(--ink)' : n.err ? '1.5px solid var(--bad)' : '1.5px solid #141414',
                  background: n.sel ? '#fffae0' : '#fff',
                  borderRadius:6, fontSize:11, textAlign:'center'
                }}>
                  <div style={{fontSize:14, marginBottom:2}}>{n.t}</div>
                  <div style={{fontWeight:600}}>{n.name}</div>
                  <div className="muted" style={{fontSize:9.5, marginTop:1}}>{n.sub}</div>
                </div>
              ))}
            </div>
          </div>

          {/* inspector */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Pull SFTP</h3>
              <div className="muted" style={{fontSize:10}}>step 2 · selected</div>
            </div>
            <div style={{padding:'10px 12px'}}>
              <FieldGroup title="Source" scope="server">
                <FieldRow state="input" label="Server"><Sel value="assessor-sftp · prod secret" /></FieldRow>
                <FieldRow state="input" label="Path"><Inp mono value="/outgoing/parcels_{YYYYMMDD}.csv" /></FieldRow>
                <FieldRow state="input" label="Auth"><Sel value="key · in vault" /></FieldRow>
              </FieldGroup>
              <FieldGroup title="Behavior">
                <FieldRow state="input" label="On missing file"><Sel value="wait + retry (15m, 5x)" /></FieldRow>
                <FieldRow state="input" label="On checksum mismatch"><Sel value="fail · notify" /></FieldRow>
                <FieldRow state="input" label="Timeout"><Inp mono value="10 min" /></FieldRow>
              </FieldGroup>
              <FieldGroup title="Outputs">
                <FieldRow state="calculated" label="Out" value="$pull.file" derivedFrom="step" />
                <FieldRow state="calculated" label="Out" value="$pull.row_count" derivedFrom="step" />
                <FieldRow state="calculated" label="Out" value="$pull.checksum" derivedFrom="step" />
              </FieldGroup>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { StudioFormAI, StudioFormBuilder, StudioWorkflowAI, StudioWorkflowEditor });
