// Studio Map · multiplayer + workspace RBAC
// 4 artboards:
//   StudioMapCollab   — map editor with presence avatars, named cursors, drawing markup, activity sidebar, follow-mode pill
//   MapCommentThread  — feature-pinned comment thread expanded as a drawer
//   RBACOverview      — model diagram + full role × permission matrix
//   TeamMembers       — workspace people, their roles, scoped invite flow

function StudioMapCollab() {
  return (
    <div className="scr">
      <TopBar crumbs={['Studio','Map','parcels-use-map']} area="studio" />
      <StudioSidebar active="map" />
      <div className="main" style={{display:'flex', flexDirection:'column', overflow:'hidden'}}>
        {/* Header with presence */}
        <div style={{padding:'10px 18px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:10}}>
          <span style={{fontSize:16}}>◐</span>
          <input readOnly className="inp" style={{width:240, fontWeight:600}} value="parcels-use-map" />
          <Badge>draft v1</Badge>
          <Badge kind="ok">autosaved 2s ago</Badge>
          <div style={{flex:1}}/>
          {/* Presence avatars */}
          <div style={{display:'inline-flex', alignItems:'center', gap:4, marginRight:8}}>
            <span className="muted" style={{fontSize:10.5, marginRight:4}}>3 here</span>
            {[
              { i:'JD', c:'#ffe55c', name:'jamie' },
              { i:'KT', c:'#2a6fdb', name:'k.tan', editing:true },
              { i:'AL', c:'#1d6b3e', name:'a.lee', commenting:true },
            ].map(p => (
              <div key={p.i} title={p.name + (p.editing ? ' · editing parcels/fill' : p.commenting ? ' · commenting' : '')} style={{
                width:24, height:24, borderRadius:'50%', background:p.c, border:'1.5px solid var(--ink)',
                display:'grid', placeItems:'center', fontSize:10, fontWeight:700, color:'#141414',
                marginLeft:-6, position:'relative', cursor:'pointer'
              }}>
                {p.i}
                {p.editing && <span style={{position:'absolute', bottom:-2, right:-2, width:8, height:8, borderRadius:'50%', background:'var(--ok)', border:'1.5px solid #fff'}}/>}
                {p.commenting && <span style={{position:'absolute', bottom:-2, right:-2, width:8, height:8, borderRadius:'50%', background:'var(--pencil)', border:'1.5px solid #fff'}}/>}
              </div>
            ))}
            <Btn ghost sm style={{marginLeft:8}}>+ Invite</Btn>
          </div>
          <Btn ghost>← Back to chat</Btn>
          <Btn kind="p">Publish…</Btn>
        </div>

        <Tabs items={[
          { k:'design', t:'Design' },
          { k:'data', t:'Data bindings', ct:1 },
          { k:'comments', t:'Comments', ct:4 },
          { k:'activity', t:'Activity' },
          { k:'access', t:'Access' },
        ]} active="design" />

        <div style={{display:'grid', gridTemplateColumns:'240px 1fr 280px', flex:1, overflow:'hidden'}}>
          {/* LEFT: layers */}
          <div style={{borderRight:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center'}}>
              <h3 style={{margin:0, fontSize:11.5}}>Layers</h3>
              <div style={{flex:1}}/>
              <Btn ghost sm>+ Layer</Btn>
            </div>
            {[
              { n:'parcels/labels', t:'symbol', on:true },
              { n:'parcels/outline', t:'line', on:true },
              { n:'parcels/fill', t:'fill', on:true, sel:true, who:'k.tan' },
              { n:'markup', t:'collab drawing', on:true, kind:'markup' },
              { n:'basemap/positron', t:'raster', on:true },
            ].map(l => (
              <div key={l.n} className="row" style={{
                padding:'5px 12px', borderBottom:'1px solid #f1f1f1',
                background: l.sel ? '#fffae0' : 'transparent',
                borderLeft: l.sel ? '3px solid var(--ink)' : '3px solid transparent',
                fontSize:11
              }}>
                <input type="checkbox" readOnly defaultChecked={l.on} />
                <span className="mono" style={{flex:1, fontWeight: l.sel ? 600 : 400}}>{l.n}</span>
                {l.who && <span style={{
                  fontSize:9, padding:'1px 5px', borderRadius:3,
                  background:'#2a6fdb', color:'#fff', fontWeight:600,
                }} title={l.who + ' is editing this layer'}>KT</span>}
                {l.kind === 'markup' && <span className="tag" style={{fontSize:9, background:'var(--accent)'}}>🖍 collab</span>}
                <span className="muted" style={{fontSize:9.5}}>{l.t}</span>
              </div>
            ))}

            <div style={{padding:'8px 12px', borderTop:'1px dashed #d8d8d8'}}>
              <div className="muted" style={{fontSize:10.5, textTransform:'uppercase', letterSpacing:'0.06em', marginBottom:4}}>Markup tools</div>
              <div className="row" style={{gap:4, flexWrap:'wrap'}}>
                {['↗ arrow','▭ rect','◯ circle','✎ free','🗒 note','▤ text'].map(t => (
                  <span key={t} className="tag" style={{cursor:'pointer'}}>{t}</span>
                ))}
              </div>
              <div className="muted" style={{fontSize:10, marginTop:4}}>markup is per-draft · doesn't touch canonical style</div>
            </div>
          </div>

          {/* CENTER: map with overlays */}
          <div style={{position:'relative', background:'#fff', overflow:'hidden'}}>
            <div style={{padding:14, position:'relative'}}>
              <MapPreview mode="layer" height={420} popup={false} scaleText="1:8,000" />

              {/* Other-user cursors */}
              <div style={{position:'absolute', left:'30%', top:'40%', pointerEvents:'none'}}>
                <svg width="20" height="20" viewBox="0 0 20 20"><path d="M 2 2 L 2 14 L 6 11 L 9 17 L 11 16 L 8 10 L 14 10 Z" fill="#2a6fdb" stroke="#fff" strokeWidth="1"/></svg>
                <div style={{
                  position:'absolute', left:18, top:14,
                  background:'#2a6fdb', color:'#fff',
                  padding:'2px 6px', borderRadius:3, fontSize:10, fontWeight:600,
                  whiteSpace:'nowrap'
                }}>k.tan · editing fill</div>
              </div>

              <div style={{position:'absolute', left:'62%', top:'58%', pointerEvents:'none'}}>
                <svg width="20" height="20" viewBox="0 0 20 20"><path d="M 2 2 L 2 14 L 6 11 L 9 17 L 11 16 L 8 10 L 14 10 Z" fill="#1d6b3e" stroke="#fff" strokeWidth="1"/></svg>
                <div style={{
                  position:'absolute', left:18, top:14,
                  background:'#1d6b3e', color:'#fff',
                  padding:'2px 6px', borderRadius:3, fontSize:10, fontWeight:600,
                  whiteSpace:'nowrap'
                }}>a.lee</div>
              </div>

              {/* Drawing markup — sticky note */}
              <div style={{
                position:'absolute', left:'50%', top:'18%',
                background:'#fff4b8', border:'1px solid #d4b14a',
                padding:'8px 10px', borderRadius:3, fontSize:11,
                maxWidth:160, transform:'rotate(-2deg)',
                boxShadow:'2px 4px 8px rgba(0,0,0,0.12)',
              }}>
                <div className="muted" style={{fontSize:9, marginBottom:2}}>a.lee · 12m ago</div>
                <div>this whole block should be R-2 not R-1</div>
              </div>

              {/* Drawing markup — arrow */}
              <svg style={{position:'absolute', left:'18%', top:'24%', pointerEvents:'none'}} width="120" height="100" viewBox="0 0 120 100">
                <defs><marker id="ar1" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto"><path d="M 0 0 L 10 5 L 0 10 z" fill="#c03b2b"/></marker></defs>
                <path d="M 10 10 Q 50 20 100 70" stroke="#c03b2b" strokeWidth="2" fill="none" markerEnd="url(#ar1)"/>
              </svg>

              {/* Comment pin on selected parcel */}
              <div style={{position:'absolute', left:'37%', top:'52%'}}>
                <div style={{
                  width:28, height:28, borderRadius:'14px 14px 14px 2px',
                  background:'var(--accent)', border:'1.5px solid var(--ink)',
                  display:'grid', placeItems:'center', fontSize:13, fontWeight:700,
                  cursor:'pointer', transform:'rotate(-8deg)',
                }} title="2 comments on 04-021-204">2</div>
              </div>

              <div style={{position:'absolute', left:'62%', top:'48%'}}>
                <div style={{
                  width:24, height:24, borderRadius:'12px 12px 12px 2px',
                  background:'#fff', border:'1.5px solid var(--ink)',
                  display:'grid', placeItems:'center', fontSize:11, fontWeight:700,
                  cursor:'pointer', transform:'rotate(-6deg)',
                }} title="1 comment">1</div>
              </div>

              {/* Follow-mode pill */}
              <div style={{
                position:'absolute', top:14, left:14,
                display:'flex', alignItems:'center', gap:6,
                padding:'4px 8px 4px 4px', background:'#2a6fdb', color:'#fff',
                borderRadius:14, fontSize:10.5, fontWeight:600, cursor:'pointer',
                boxShadow:'0 2px 8px rgba(0,0,0,0.18)'
              }}>
                <span style={{width:20,height:20,borderRadius:'50%',background:'#2a6fdb',border:'1.5px solid #fff',display:'grid',placeItems:'center',fontSize:9}}>KT</span>
                <span>Following k.tan · ⎋ to stop</span>
              </div>
            </div>

            <div className="row" style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', background:'#fafafa', gap:6, fontSize:10.5}}>
              <span className="muted">Edits apply live to all collaborators · 1k sampled features</span>
              <div style={{flex:1}}/>
              <FiltChip on>labels</FiltChip>
              <FiltChip on>popups</FiltChip>
              <FiltChip on>markup · 3</FiltChip>
            </div>
          </div>

          {/* RIGHT: live activity */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto', display:'flex', flexDirection:'column'}}>
            <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', background:'#fff'}}>
              <div className="row">
                <h3 style={{margin:0, fontSize:11.5}}>Activity · live</h3>
                <div style={{flex:1}}/>
                <span style={{width:7, height:7, borderRadius:'50%', background:'var(--ok)'}}/>
                <span className="muted" style={{fontSize:10}}>3 here</span>
              </div>
            </div>
            <div style={{padding:'10px 12px', overflow:'auto', flex:1, display:'flex', flexDirection:'column', gap:8, fontSize:11}}>
              {[
                { who:'k.tan', c:'#2a6fdb', t:'just now', a:'editing fill-opacity 0.85 → 0.88' },
                { who:'a.lee', c:'#1d6b3e', t:'12m', a:'added sticky note · "block should be R-2"' },
                { who:'a.lee', c:'#1d6b3e', t:'14m', a:'commented on parcel 04-021-204' },
                { who:'k.tan', c:'#2a6fdb', t:'18m', a:'drew arrow · markup layer' },
                { who:'jamie', c:'#ffe55c', t:'1h', a:'changed renderer to class-breaks' },
                { who:'jamie', c:'#ffe55c', t:'1h', a:'created draft v1 from prompt' },
              ].map((a,i) => (
                <div key={i} className="row" style={{gap:6, padding:'4px 6px', background:'#fff', borderRadius:4, border:'1px solid #e4e4e4'}}>
                  <span style={{width:18, height:18, borderRadius:'50%', background:a.c, border:'1px solid var(--ink)', flexShrink:0, display:'grid', placeItems:'center', fontSize:8, fontWeight:700, color:'#141414'}}>
                    {a.who.split('').filter(x => /[A-Z.]/i.test(x)).join('').toUpperCase().slice(0,2)}
                  </span>
                  <div style={{flex:1, minWidth:0}}>
                    <div style={{fontSize:10.5}}><b>{a.who}</b> {a.a}</div>
                    <div className="muted" style={{fontSize:9}}>{a.t}</div>
                  </div>
                </div>
              ))}
            </div>
            <div style={{padding:'8px 12px', borderTop:'1px solid #e4e4e4', background:'#fff', display:'flex', gap:4}}>
              <input readOnly className="inp" style={{flex:1, fontSize:11}} placeholder="@mention or quick comment…" />
              <Btn kind="p" sm>↵</Btn>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function MapCommentThread() {
  return (
    <div className="scr" style={{position:'relative'}}>
      <TopBar crumbs={['Studio','Map','parcels-use-map']} area="studio" />
      <StudioSidebar active="map" />
      <div className="main">
        <PageHead title="parcels-use-map" sub="comment thread open · drawer" />
        <div style={{padding:14, background:'#fafafa', flex:1, opacity:0.45, pointerEvents:'none'}}>
          <MapPreview mode="layer" height={420} popup={false} scaleText="1:8,000" />
        </div>
      </div>

      {/* Comment drawer */}
      <div className="drawer" style={{width:440}}>
        <h2>
          <span style={{
            width:22, height:22, borderRadius:'11px 11px 11px 2px',
            background:'var(--accent)', border:'1.5px solid var(--ink)',
            display:'grid', placeItems:'center', fontSize:11, fontWeight:700,
          }}>2</span>
          <span style={{fontSize:13, fontWeight:600, marginLeft:6}}>Parcel 04-021-204</span>
          <div style={{flex:1}}/>
          <span className="muted mono" style={{fontSize:10}}>thread_2f81</span>
          <span style={{cursor:'pointer'}}>×</span>
        </h2>

        <div style={{padding:'8px 14px', background:'#fafafa', borderBottom:'1px solid #e4e4e4', fontSize:11}}>
          <div className="row" style={{gap:6, marginBottom:3}}>
            <span className="muted">anchored to</span>
            <span className="tag">feature · parcel 04-021-204</span>
            <span className="tag">layer · parcels/fill</span>
          </div>
          <div className="muted" style={{fontSize:10}}>survives map edits · re-anchors if feature moves</div>
        </div>

        <div style={{flex:1, overflow:'auto', padding:'12px 14px', display:'flex', flexDirection:'column', gap:14}}>
          {/* msg 1 */}
          <div>
            <div className="row" style={{marginBottom:4}}>
              <span style={{width:22, height:22, borderRadius:'50%', background:'#1d6b3e', border:'1.5px solid var(--ink)', display:'grid', placeItems:'center', fontSize:9, fontWeight:700}}>AL</span>
              <b style={{fontSize:11.5, marginLeft:6}}>a.lee</b>
              <span className="muted" style={{fontSize:10, marginLeft:6}}>14m ago</span>
              <div style={{flex:1}}/>
              <span style={{fontSize:11, color:'#888', cursor:'pointer'}}>⋯</span>
            </div>
            <div style={{padding:'8px 10px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:5, fontSize:11.5, lineHeight:1.5}}>
              <span style={{background:'#eaf0fb', color:'#1f3f87', padding:'1px 4px', borderRadius:2, fontWeight:600}}>@jamie</span> this parcel was reclassified to R-2 last month — the assessor file was updated but the source table is stale. Can we refresh from prod-postgis?
            </div>
            <div className="row" style={{gap:4, marginTop:4}}>
              {[['👀','2'],['👍','1']].map(([e,n]) => (
                <span key={e} style={{fontSize:11, padding:'1px 6px', border:'1px solid #d8d8d8', borderRadius:10, background:'#fff', cursor:'pointer'}}>{e} {n}</span>
              ))}
              <span style={{fontSize:11, padding:'1px 6px', border:'1px dashed #c5c5c5', borderRadius:10, background:'#fff', cursor:'pointer', color:'#888'}}>+ react</span>
            </div>
          </div>

          {/* msg 2 */}
          <div>
            <div className="row" style={{marginBottom:4}}>
              <span style={{width:22, height:22, borderRadius:'50%', background:'#ffe55c', border:'1.5px solid var(--ink)', display:'grid', placeItems:'center', fontSize:9, fontWeight:700}}>JD</span>
              <b style={{fontSize:11.5, marginLeft:6}}>jamie</b>
              <span className="muted" style={{fontSize:10, marginLeft:6}}>8m ago</span>
            </div>
            <div style={{padding:'8px 10px', background:'#fffae0', border:'1.2px solid var(--accent-deep)', borderRadius:5, fontSize:11.5, lineHeight:1.5}}>
              Confirmed — refresh ran 4d ago but didn't pick up that change. I'll trigger a manual sync. Will reply when it lands.
            </div>
          </div>

          {/* attachment — linked job */}
          <div style={{padding:'6px 10px', background:'#fafafa', border:'1px dashed #c5c5c5', borderRadius:4, fontSize:10.5}}>
            <div className="row">
              <span style={{color:'var(--ok)'}}>✓</span>
              <span style={{flex:1, marginLeft:6}}>linked job <span className="mono">job_4822 · refresh prod-postgis/parcels_2024</span></span>
              <span style={{color:'var(--ok)'}}>completed 2m</span>
            </div>
          </div>

          {/* msg 3 */}
          <div>
            <div className="row" style={{marginBottom:4}}>
              <span style={{width:22, height:22, borderRadius:'50%', background:'#ffe55c', border:'1.5px solid var(--ink)', display:'grid', placeItems:'center', fontSize:9, fontWeight:700}}>JD</span>
              <b style={{fontSize:11.5, marginLeft:6}}>jamie</b>
              <span className="muted" style={{fontSize:10, marginLeft:6}}>just now</span>
            </div>
            <div style={{padding:'8px 10px', background:'#fff', border:'1px solid #e4e4e4', borderRadius:5, fontSize:11.5, lineHeight:1.5}}>
              Done — use_code is now R-2. Should re-render on next viewport refresh.
            </div>
          </div>
        </div>

        <div style={{padding:'10px 14px', borderTop:'1px solid #e4e4e4', background:'#fafafa'}}>
          <textarea readOnly className="inp" style={{height:40, padding:6, fontSize:11.5, resize:'none'}} placeholder="Reply · type @ to mention" />
          <div className="row" style={{marginTop:6, gap:4}}>
            <Btn ghost sm>📎 Attach</Btn>
            <Btn ghost sm>🔗 Link object</Btn>
            <div style={{flex:1}}/>
            <Btn ghost sm>Mark resolved</Btn>
            <Btn kind="p" sm>Send ⌘↵</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

function RBACOverview() {
  return (
    <div className="scr">
      <TopBar crumbs={['Settings','Access · RBAC']} area="operate" />
      <Sidebar active="access" />
      <div className="main">
        <PageHead
          title="Access · roles & permissions"
          sub="Hierarchical RBAC. Workspace roles inherit down; per-content and per-publication overrides allowed."
          actions={<><Btn>Roles audit</Btn><Btn kind="p">+ Custom role</Btn></>}
        />

        {/* Model strip */}
        <div style={{padding:'14px 18px', background:'#fafafa', borderBottom:'1px solid #e4e4e4'}}>
          <h3 style={{margin:'0 0 8px', fontSize:12}}>Permission scope hierarchy</h3>
          <div className="row" style={{gap:0, alignItems:'stretch'}}>
            {[
              { i:'🏢', t:'Workspace', d:'the cohort of envs · default role lives here', c:'#fffae0' },
              { i:'◷', t:'Environment', d:'dev / staging / prod · can scope a role to one env', c:'#eaf0fb' },
              { i:'▤', t:'Content', d:'map / dashboard / report / data resource', c:'#ecf7f0' },
              { i:'◈', t:'Publication', d:'service slot / share link / catalog entry', c:'#fcf3df' },
              { i:'⚙', t:'Resource field', d:'PII redaction, edit-this-field-only', c:'#fff' },
            ].map((s,i,arr) => (
              <React.Fragment key={s.t}>
                <div style={{
                  flex:1, padding:'10px 12px', background:s.c, border:'1.2px solid #d8d8d8', borderRadius:5,
                  display:'flex', flexDirection:'column', gap:3, position:'relative'
                }}>
                  <div className="row" style={{gap:6}}>
                    <span style={{fontSize:14}}>{s.i}</span>
                    <b style={{fontSize:12}}>{s.t}</b>
                  </div>
                  <div className="muted" style={{fontSize:10.5, lineHeight:1.4}}>{s.d}</div>
                </div>
                {i < arr.length - 1 && <div style={{display:'grid', placeItems:'center', padding:'0 6px', color:'#888', fontSize:14}}>›</div>}
              </React.Fragment>
            ))}
          </div>
          <div className="muted" style={{fontSize:10.5, marginTop:6}}>Default flows top → bottom. Overrides at any level take precedence.</div>
        </div>

        <Toolbar
          filters={<>
            <FiltChip on x>scope: workspace</FiltChip>
            <FiltChip>built-in only</FiltChip>
            <FiltChip>custom</FiltChip>
          </>}
          right={<span className="muted" style={{fontSize:11}}>8 built-in roles · 2 custom · 14 members affected</span>}
        />

        {/* Roles × permissions matrix */}
        <div style={{overflow:'auto', flex:1}}>
          <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
            <thead><tr>
              <th style={{minWidth:180}}>Role</th>
              <th>View public</th>
              <th>View internal</th>
              <th>Comment</th>
              <th>Draft / collab</th>
              <th>Publish</th>
              <th>Edit access</th>
              <th>Manage roles</th>
              <th>Server admin</th>
            </tr></thead>
            <tbody>
              <tr><td><b>Admin</b><div className="muted" style={{fontSize:9.5}}>server + workspace</div></td>
                {[1,1,1,1,1,1,1,1].map((v,i)=><td key={i}><Badge kind="ok">●</Badge></td>)}
              </tr>
              <tr><td><b>Workspace owner</b><div className="muted" style={{fontSize:9.5}}>per workspace</div></td>
                {[1,1,1,1,1,1,1,0].map((v,i)=><td key={i}>{v ? <Badge kind="ok">●</Badge> : <span className="muted">—</span>}</td>)}
              </tr>
              <tr><td><b>Publisher</b><div className="muted" style={{fontSize:9.5}}>creates + publishes content</div></td>
                {[1,1,1,1,1,'env',0,0].map((v,i)=><td key={i}>{v === 1 ? <Badge kind="ok">●</Badge> : v === 'env' ? <Badge>env-scoped</Badge> : <span className="muted">—</span>}</td>)}
              </tr>
              <tr><td><b>Editor</b><div className="muted" style={{fontSize:9.5}}>drafts only · no publish</div></td>
                {[1,1,1,1,0,0,0,0].map((v,i)=><td key={i}>{v ? <Badge kind="ok">●</Badge> : <span className="muted">—</span>}</td>)}
              </tr>
              <tr style={{background:'#fffae0'}}><td><b>Draft collaborator</b> <span className="tag" style={{fontSize:9}}>new</span><div className="muted" style={{fontSize:9.5}}>scoped to specific drafts only</div></td>
                {[1,1,1,'scoped',0,0,0,0].map((v,i)=><td key={i}>{v === 1 ? <Badge kind="ok">●</Badge> : v === 'scoped' ? <Badge kind="accent">scoped</Badge> : <span className="muted">—</span>}</td>)}
              </tr>
              <tr><td><b>Reviewer</b><div className="muted" style={{fontSize:9.5}}>comment + approve only</div></td>
                {[1,1,1,0,0,0,0,0].map((v,i)=><td key={i}>{v ? <Badge kind="ok">●</Badge> : <span className="muted">—</span>}</td>)}
              </tr>
              <tr><td><b>Org viewer</b><div className="muted" style={{fontSize:9.5}}>signed-in workspace member</div></td>
                {[1,1,0,0,0,0,0,0].map((v,i)=><td key={i}>{v ? <Badge kind="ok">●</Badge> : <span className="muted">—</span>}</td>)}
              </tr>
              <tr><td><b>Anonymous</b><div className="muted" style={{fontSize:9.5}}>public-read only</div></td>
                {[1,0,0,0,0,0,0,0].map((v,i)=><td key={i}>{v ? <Badge kind="ok">●</Badge> : <span className="muted">—</span>}</td>)}
              </tr>
              <tr style={{borderTop:'2px solid #d8d8d8'}}><td><b>GIS team</b> <span className="tag" style={{fontSize:9}}>custom</span><div className="muted" style={{fontSize:9.5}}>publisher + can edit access</div></td>
                {[1,1,1,1,1,1,1,0].map((v,i)=><td key={i}>{v ? <Badge kind="info">●</Badge> : <span className="muted">—</span>}</td>)}
              </tr>
              <tr><td><b>Auditor</b> <span className="tag" style={{fontSize:9}}>custom</span><div className="muted" style={{fontSize:9.5}}>read internal + audit log + no PII</div></td>
                <td><Badge kind="ok">●</Badge></td>
                <td><Badge kind="info">no PII</Badge></td>
                <td><Badge kind="ok">●</Badge></td>
                {[0,0,0,0,0].map((v,i)=><td key={i}><span className="muted">—</span></td>)}
              </tr>
            </tbody>
          </table>
        </div>

        <div style={{padding:'8px 18px', borderTop:'1px solid #e4e4e4', background:'#fafafa', display:'flex', alignItems:'center', gap:14, fontSize:10.5}}>
          <span><Badge kind="ok">●</Badge> granted</span>
          <span><Badge kind="info">●</Badge> custom · conditional</span>
          <span><Badge kind="accent">scoped</Badge> per-content scope only</span>
          <span><Badge>env-scoped</Badge> grants only in chosen envs</span>
          <span className="muted">—</span><span>not granted</span>
          <div style={{flex:1}}/>
          <Ann red>built-in roles can't be modified. clone to create a custom variant.</Ann>
        </div>
      </div>
    </div>
  );
}

function TeamMembers() {
  return (
    <div className="scr">
      <TopBar crumbs={['Settings','Access','Members']} area="operate" />
      <Sidebar active="access" />
      <div className="main">
        <PageHead
          title="Members · Public Works"
          sub="14 people · grouped by role · invite by email, scope by workspace / env / content"
          actions={<>
            <Btn>Export CSV</Btn>
            <Btn kind="p">+ Invite</Btn>
          </>}
        />

        <div style={{display:'grid', gridTemplateColumns:'1fr 380px', flex:1, overflow:'hidden'}}>
          {/* members list */}
          <div style={{overflow:'auto'}}>
            <Toolbar
              filters={<>
                <FiltChip on x>state: active</FiltChip>
                <FiltChip>role: any</FiltChip>
                <FiltChip>scope: any</FiltChip>
                <input className="inp" style={{width:200, height:22}} placeholder="Search…" readOnly />
              </>}
              right={<span className="muted" style={{fontSize:11}}>14 active · 2 pending · 1 deactivated</span>}
            />
            <table className="tbl tbl--cmpt">
              <thead><tr>
                <th>Member</th><th>Role</th><th>Scope</th><th>Last active</th><th>Source</th><th></th>
              </tr></thead>
              <tbody>
                {[
                  { n:'jamie', e:'jamie@example.gov', c:'#ffe55c', r:'Workspace owner', s:'all envs', t:'now', src:'OIDC · Entra' },
                  { n:'k.tan', e:'k.tan@example.gov', c:'#2a6fdb', r:'GIS team', s:'all envs', t:'2m', src:'OIDC · Entra', custom:true },
                  { n:'a.lee', e:'a.lee@example.gov', c:'#1d6b3e', r:'Editor', s:'all envs', t:'14m', src:'OIDC · Entra' },
                  { n:'m.silva', e:'m.silva@example.gov', c:'#c03b2b', r:'Reviewer', s:'all envs', t:'1h', src:'OIDC · Entra' },
                  { n:'partner-readonly', e:'api-key', c:'#888', r:'Org viewer', s:'prod only', t:'2m', src:'API key', kind:'key' },
                  { n:'auditor-ext', e:'audit@thirdparty.com', c:'#d97706', r:'Auditor', s:'prod only', t:'2d', src:'SAML · invited', custom:true },
                  { n:'field-team', e:'group · 4 members', c:'#7ca7b3', r:'Editor', s:'dev only', t:'1h', src:'Entra group', group:true },
                ].map((m,i) => (
                  <tr key={i}>
                    <td>
                      <div className="row">
                        <span style={{width:22, height:22, borderRadius:'50%', background:m.c, border:'1.5px solid var(--ink)', display:'grid', placeItems:'center', fontSize:9, fontWeight:700, color:'#141414'}}>
                          {m.kind === 'key' ? '🔑' : m.group ? '👥' : m.n.split('').filter(x => /[A-Z.]/i.test(x)).join('').toUpperCase().slice(0,2)}
                        </span>
                        <div style={{marginLeft:8}}>
                          <div style={{fontWeight:600}}>{m.n}</div>
                          <div className="muted" style={{fontSize:9.5}}>{m.e}</div>
                        </div>
                      </div>
                    </td>
                    <td>
                      {m.custom ? <Badge kind="info">{m.r}</Badge> : <span>{m.r}</span>}
                    </td>
                    <td className="mono" style={{fontSize:10}}>{m.s}</td>
                    <td className="muted">{m.t}</td>
                    <td className="muted">{m.src}</td>
                    <td><span style={{cursor:'pointer'}}>⋯</span></td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div style={{padding:'14px 18px', borderTop:'1px dashed #d8d8d8'}}>
              <h3 style={{margin:'0 0 6px', fontSize:12}}>Pending invitations · 2</h3>
              <table className="tbl tbl--cmpt" style={{fontSize:11}}>
                <tbody>
                  <tr><td>r.kim@example.gov</td><td><Badge>Editor</Badge></td><td className="mono">all envs</td><td className="muted">sent 2d · expires in 5d</td><td><Btn ghost sm>Resend</Btn></td></tr>
                  <tr><td>tile-warmer (machine)</td><td><Badge>Custom · tile-warm</Badge></td><td className="mono">prod only · 2 services</td><td className="muted">sent 1h</td><td><Btn ghost sm>Resend</Btn></td></tr>
                </tbody>
              </table>
            </div>
          </div>

          {/* invite drawer */}
          <div style={{borderLeft:'1px solid #e4e4e4', background:'#fafafa', overflow:'auto', display:'flex', flexDirection:'column'}}>
            <div style={{padding:'10px 14px', borderBottom:'1px solid #e4e4e4', background:'var(--accent)'}}>
              <div className="row">
                <span style={{fontSize:14}}>＋</span>
                <b style={{fontSize:13}}>Invite to Public Works</b>
              </div>
              <div className="muted" style={{fontSize:10.5, marginTop:2}}>scoped per-env or per-content if you want</div>
            </div>
            <div style={{padding:'12px 14px', display:'flex', flexDirection:'column', gap:10, overflow:'auto', flex:1}}>
              <FieldGroup title="Identity" scope="resource">
                <FieldRow state="input" label="Email or group"><Inp value="r.kim@example.gov" /></FieldRow>
                <FieldRow state="input" label="Send via"><Sel value="Email · OIDC sign-in link" /></FieldRow>
              </FieldGroup>

              <FieldGroup title="Role" scope="resource">
                <FieldRow state="input" label="Base role">
                  <Sel value="Editor (drafts only, no publish)" />
                </FieldRow>
                <FieldRow state="discovered" label="What this grants" hint="derived from role definition">
                  <div className="col" style={{gap:3, fontSize:10.5}}>
                    <div className="row"><Badge kind="ok">●</Badge><span style={{marginLeft:6}}>View public + internal</span></div>
                    <div className="row"><Badge kind="ok">●</Badge><span style={{marginLeft:6}}>Comment + react</span></div>
                    <div className="row"><Badge kind="ok">●</Badge><span style={{marginLeft:6}}>Draft + collab edit</span></div>
                    <div className="row"><span className="muted" style={{marginLeft:18}}>— no publish · no access edit · no admin</span></div>
                  </div>
                </FieldRow>
              </FieldGroup>

              <FieldGroup title="Scope" scope="publication">
                <FieldRow state="input" label="Environments">
                  <div className="col" style={{gap:3, fontSize:11}}>
                    <label className="row" style={{gap:6}}><input type="checkbox" readOnly defaultChecked/><span>dev</span></label>
                    <label className="row" style={{gap:6}}><input type="checkbox" readOnly/><span>staging</span></label>
                    <label className="row" style={{gap:6}}><input type="checkbox" readOnly/><span>prod</span></label>
                  </div>
                </FieldRow>
                <FieldRow state="input" label="Content scope (optional)">
                  <div className="col" style={{gap:3, fontSize:11}}>
                    <label className="row" style={{gap:6}}><input type="radio" readOnly defaultChecked/><span>All content in chosen envs</span></label>
                    <label className="row" style={{gap:6}}><input type="radio" readOnly/><span>Only specific items…</span></label>
                  </div>
                </FieldRow>
              </FieldGroup>

              <FieldGroup title="Limits">
                <FieldRow state="input" label="Expires"><Sel value="in 30 days · renew required" /></FieldRow>
                <FieldRow state="input" label="MFA required"><label className="row" style={{gap:6, fontSize:11}}><input type="checkbox" readOnly defaultChecked/><span>yes</span></label></FieldRow>
                <FieldRow state="input" label="IP allowlist"><Inp value="(none · global)" /></FieldRow>
              </FieldGroup>

              <Callout kind="info">
                Invitation creates a scoped role binding. Revoke or narrow scope any time. Every change is audited.
              </Callout>
            </div>
            <div style={{padding:'10px 14px', borderTop:'1px solid #e4e4e4', background:'#fff', display:'flex', gap:6}}>
              <Btn ghost sm>Cancel</Btn>
              <div style={{flex:1}}/>
              <Btn sm>Save as template</Btn>
              <Btn kind="p" sm>Send invite</Btn>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { StudioMapCollab, MapCommentThread, RBACOverview, TeamMembers });
