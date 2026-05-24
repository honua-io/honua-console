// HIGH-FIDELITY FIELD-LEVEL SCREENS — anchored to metadata-v2-admin-input-model.md
// Each screen uses the field-state vocabulary (FieldRow, FieldGroup, ScopeChip).
// Three screens here:
//   1. ResSourceHi      — Resource → Source with introspection panel
//   2. ResFieldsHi      — Resource → Fields grid with all state combos
//   3. FieldStateGuide  — teaching screen: vocabulary + worked examples

function ResSourceHi() {
  // Resource → Source. Shows after a "Browse storage" auto-discovery.
  // Most fields are 🔍 discovered with override links. Some 🧮 calculated. Some ⚙️ system.
  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024','Source']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead />
        <SuperTabs on="define" sub="source" />
        <FieldStateLegend />

        <div style={{padding:'14px 18px', overflow:'auto', flex:1, display:'grid', gridTemplateColumns:'1fr 320px', gap:14}}>
          <div className="col">

            {/* WHERE IT LIVES (input + system) */}
            <FieldGroup
              title="Storage binding"
              sub="how Honua reaches this dataset"
              scope="resource"
              count="3 layers across 2 services"
            >
              <FieldRow state="input"      label="Binding name"          value="primary" hint="display name for this binding" />
              <FieldRow state="input"      label="Connection"            value="prod-postgis" hint="postgres · v16" mono override={false}>
                <div className="sel" style={{flex:1}}><Inp value="prod-postgis" /></div>
              </FieldRow>
              <FieldRow state="input"      label="Storage type"          value="RelationalTable" hint="filtered by Connection.Type"
                children={<div className="sel" style={{flex:1}}><Inp value="Relational table" /></div>} />
              <FieldRow state="input"      label="Locator"               value="public.parcels_2024" mono hint="schema.table · use Browse to pick">
                <Inp mono value="public.parcels_2024" />
                <Btn ghost sm>Browse…</Btn>
              </FieldRow>
              <FieldRow state="system"     label="Binding ID"            value="sb_01HZX7…84A" />
              <FieldRow state="discovered" label="Storage layer ID"      value="0" mono override revertable hint="auto-assigned at bind time" />
              <FieldRow state="discovered" label="Capabilities"          hint="derived from storage type + provider">
                <div style={{display:'flex', gap:3, flexWrap:'wrap'}}>
                  {['Query','Filter','Sort','Aggregate','Tile','Download','Search'].map(c => (
                    <Badge key={c} kind="ok">{c}</Badge>
                  ))}
                  {['Edit','Transactions','Render'].map(c => (
                    <Badge key={c}>{c} · off</Badge>
                  ))}
                </div>
              </FieldRow>
            </FieldGroup>

            {/* SPATIAL — most fields auto-discovered */}
            <FieldGroup
              title="Spatial reference"
              sub="auto-discovered from the table; you can override if you know better"
              scope="resource"
              count="3 layers"
            >
              <FieldRow state="discovered" label="SRID"                 value="4326" mono override revertable hint="from ST_SRID(geom) on first 1k rows" />
              <FieldRow state="calculated" label="CRS"                  value="http://www.opengis.net/def/crs/EPSG/0/4326" mono derivedFrom="SRID" />
              <FieldRow state="calculated" label="Is geographic"        value="true" derivedFrom="SRID" />
              <FieldRow state="discovered" label="Geometry type"        value="MultiPolygon" override revertable hint="100% of sample rows · single type" />
              <FieldRow state="discovered" label="Primary geometry field" value="geom" mono override hint="only one geometry column found" />
              <FieldRow state="discovered" label="Bounding box (W,S,E,N)" hint="computed via ST_Extent · 14m ago"
                children={<>
                  <span className="mono" style={{fontSize:10.5}}>-124.4, 32.5, -114.1, 42.0</span>
                  <Btn ghost sm>Recompute</Btn>
                  <span className="fs-override">Override</span>
                </>} />
            </FieldGroup>

            {/* TEMPORAL */}
            <FieldGroup
              title="Temporal"
              sub="off by default · turn on if this data has a time dimension"
              scope="resource"
              count="3 layers"
            >
              <FieldRow state="input" label="Time-aware">
                <label className="row" style={{gap:6, fontSize:11}}>
                  <input type="checkbox" readOnly defaultChecked />
                  <span>enabled</span>
                </label>
              </FieldRow>
              <FieldRow state="input"      label="Start time field" hint="picker filtered to Date/DateTime"
                children={<div className="sel" style={{flex:1}}><Inp value="last_assessment" /></div>} />
              <FieldRow state="input"      label="End time field"   hint="leave blank if instantaneous"
                children={<div className="sel" style={{flex:1}}><Inp value="(none)" /></div>} />
              <FieldRow state="input"      label="Track ID field"   hint="for trajectories · usually blank"
                children={<div className="sel" style={{flex:1}}><Inp value="(none)" /></div>} />
              <FieldRow state="discovered" label="Extent start"     value="2024-01-04 00:00 UTC" mono override hint="min(last_assessment)" />
              <FieldRow state="discovered" label="Extent end"       value="2024-12-29 18:42 UTC" mono override hint="max(last_assessment)" />
            </FieldGroup>

          </div>

          {/* RIGHT COL — INTROSPECTION PANEL */}
          <div className="col">
            <Callout kind="info">
              <b>Auto-discovered from this table 14 minutes ago.</b> The 🔍 fields below were filled in by reading the source. You can override any of them — Honua remembers your override and won't clobber it on re-introspection.
            </Callout>

            <div className="card" style={{padding:0}}>
              <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex', alignItems:'center', gap:6, background:'#ecf7f0'}}>
                <span>🔍</span>
                <b style={{fontSize:11}}>Introspection</b>
                <div style={{flex:1}}/>
                <span className="muted" style={{fontSize:10}}>14m ago</span>
              </div>
              <div style={{padding:'8px 12px', fontSize:10.5, lineHeight:1.6}}>
                <div className="row"><span style={{flex:1}}>schema</span><span className="mono">public</span></div>
                <div className="row"><span style={{flex:1}}>table</span><span className="mono">parcels_2024</span></div>
                <div className="row"><span style={{flex:1}}>row count</span><span className="mono">1,284,021</span></div>
                <div className="row"><span style={{flex:1}}>columns scanned</span><span className="mono">24</span></div>
                <div className="row"><span style={{flex:1}}>geometry column</span><span className="mono">geom</span></div>
                <div className="row"><span style={{flex:1}}>id column</span><span className="mono">gid</span></div>
                <div className="row"><span style={{flex:1}}>spatial index</span><span style={{color:'var(--ok)'}}>✓ found</span></div>
                <div className="row"><span style={{flex:1}}>SRID</span><span className="mono">4326</span></div>
                <div className="row"><span style={{flex:1}}>sample size</span><span className="mono">1,000</span></div>
                <div className="divider" />
                <div className="row" style={{marginTop:4}}>
                  <span className="muted">3 of your overrides held</span>
                  <div style={{flex:1}}/>
                  <a className="fs-override" style={{fontSize:10}}>view</a>
                </div>
              </div>
              <div style={{padding:'6px 12px', borderTop:'1px solid #e4e4e4', display:'flex', gap:6}}>
                <Btn ghost sm>Re-introspect</Btn>
                <Btn ghost sm>Diff</Btn>
                <div style={{flex:1}}/>
              </div>
            </div>

            <div className="card">
              <h3>Sample rows</h3>
              <table className="tbl tbl--cmpt" style={{fontSize:10}}>
                <thead><tr><th>gid</th><th>parcel_id</th><th>area</th></tr></thead>
                <tbody>
                  <tr><td>1</td><td className="mono">04-021-118</td><td className="mono">2,148</td></tr>
                  <tr><td>2</td><td className="mono">04-021-119</td><td className="mono">1,902</td></tr>
                  <tr><td>3</td><td className="mono">04-021-120</td><td className="mono">3,012</td></tr>
                </tbody>
              </table>
              <Btn ghost sm>Open SQL preview…</Btn>
            </div>

            <Ann red>operator overrides survive re-introspection. discovered-only values get refreshed when you click Re-introspect.</Ann>
          </div>
        </div>
      </div>
    </div>
  );
}

function ResFieldsHi() {
  // Resource → Fields. The big grid. Every cell shows its state visually.
  // Fields per the doc: Name 🔍 locked, Type 🔍 locked, Title ✏️, Alias ✏️, Description ✏️,
  // Nullable 🔍, Editable ✏️, Length 🔍, DefaultValue ✏️, Domain ✏️, SemanticRoles ✏️, SqlType 🔍
  const fields = [
    {
      sel: false, role: 'id.primary', state: 'ok',
      name: 'gid', sqlType: 'int8', type: 'Integer',
      title: 'gid', alias: '', desc: '',
      nullable: false, editable: false, length: null, defaultV: '',
      domain: null, roles: ['id.primary'],
    },
    {
      sel: true, role: '', state: 'ok',
      name: 'parcel_id', sqlType: 'varchar(32)', type: 'String',
      title: 'Parcel ID', alias: 'Parcel ID', desc: 'Assessor parcel number',
      nullable: false, editable: false, length: 32, defaultV: '',
      domain: null, roles: ['display.label'],
    },
    {
      sel: false, role: '', state: 'ok',
      name: 'area_m2', sqlType: 'float8', type: 'Double',
      title: 'area_m2', alias: 'Area (m²)', desc: '',
      nullable: true, editable: true, length: null, defaultV: '',
      domain: null, roles: [],
    },
    {
      sel: false, role: '', state: 'ok',
      name: 'use_code', sqlType: 'varchar(8)', type: 'String',
      title: 'use_code', alias: 'Use', desc: '12 distinct values',
      nullable: true, editable: true, length: 8, defaultV: '',
      domain: 'coded · 12 values', roles: ['category'],
    },
    {
      sel: false, role: '', state: 'warn',
      name: 'owner_name', sqlType: 'varchar(120)', type: 'String',
      title: 'Owner', alias: '', desc: 'PII — hidden by default',
      nullable: true, editable: false, length: 120, defaultV: '',
      domain: null, roles: ['sensitive'],
    },
    {
      sel: false, role: 'temporal.start', state: 'ok',
      name: 'last_assessment', sqlType: 'date', type: 'Date',
      title: 'Last assessed', alias: 'Last assessed', desc: '',
      nullable: true, editable: true, length: null, defaultV: '',
      domain: null, roles: ['temporal.start'],
    },
    {
      sel: false, role: '', state: 'ok',
      name: 'assessed_value', sqlType: 'numeric(12,2)', type: 'Decimal',
      title: 'assessed_value', alias: 'Assessed (USD)', desc: '',
      nullable: true, editable: true, length: null, defaultV: '0',
      domain: 'range · 0..∞', roles: [],
    },
    {
      sel: false, role: 'editor.created', state: 'ok',
      name: 'created_user', sqlType: 'varchar(64)', type: 'String',
      title: 'created_user', alias: 'Created by', desc: 'audit',
      nullable: true, editable: false, length: 64, defaultV: '',
      domain: null, roles: ['editor.creator'],
    },
    {
      sel: false, role: 'editor.createdAt', state: 'ok',
      name: 'created_date', sqlType: 'timestamp', type: 'DateTime',
      title: 'created_date', alias: 'Created on', desc: 'audit',
      nullable: true, editable: false, length: null, defaultV: 'now()',
      domain: null, roles: ['editor.createdAt'],
    },
    {
      sel: false, role: 'geometry.primary', state: 'warn',
      name: 'geom', sqlType: 'geometry(MultiPolygon, 4326)', type: 'Geometry',
      title: 'geom', alias: '', desc: 'spatial column · indexed',
      nullable: false, editable: false, length: null, defaultV: '',
      domain: null, roles: ['geometry.primary'],
    },
  ];

  // tiny cell helpers
  const Discovered = ({ children, lock }) => (
    <span style={{
      display:'inline-flex', alignItems:'center', gap:4,
      padding:'1px 6px', background:'#fafdf6',
      border: '1px dashed #c4d8b6', borderRadius:3,
      fontFamily:'var(--mono)', fontSize:10.5, color:'#3a5a30',
    }}>
      <span style={{fontSize:9}}>{lock ? '🔒' : '🔍'}</span>
      {children}
    </span>
  );

  return (
    <div className="scr">
      <TopBar crumbs={['Resources','parcels_2024','Fields']} />
      <Sidebar active="resources" />
      <div className="main">
        <ResHead status="warn" />
        <SuperTabs on="define" sub="fields" />
        <FieldStateLegend />

        {/* toolbar above grid */}
        <Toolbar
          filters={<>
            <FiltChip on x>scope: published</FiltChip>
            <FiltChip>has issue</FiltChip>
            <FiltChip>has override</FiltChip>
            <FiltChip>role: any</FiltChip>
            <input className="inp" style={{width:180, height:22}} placeholder="Filter 24 fields…" readOnly />
          </>}
          right={<>
            <ScopeChip scope="resource" count="3 layers across 2 services" />
            <Btn ghost sm>Re-introspect</Btn>
            <Btn ghost sm>Import schema…</Btn>
            <Btn kind="p" sm>+ Computed field</Btn>
          </>}
        />

        <div style={{overflow:'auto',flex:1}}>
          <table className="tbl tbl--cmpt" style={{fontSize:10.5}}>
            <thead>
              <tr>
                <th style={{width:18}}><input type="checkbox" readOnly /></th>
                <th>Name <span style={{color:'#999',fontSize:9}}>🔍</span></th>
                <th>SQL type <span style={{color:'#999',fontSize:9}}>🔍</span></th>
                <th>Type <span style={{color:'#999',fontSize:9}}>🔍</span></th>
                <th>Title</th>
                <th>Alias</th>
                <th>Nullable <span style={{color:'#999',fontSize:9}}>🔍</span></th>
                <th>Editable</th>
                <th>Length <span style={{color:'#999',fontSize:9}}>🔍</span></th>
                <th>Default</th>
                <th>Domain</th>
                <th>Semantic roles</th>
                <th style={{width:60}}></th>
              </tr>
            </thead>
            <tbody>
              {fields.map((f, i) => (
                <tr key={f.name} style={f.state === 'warn' ? {background:'#fff7e6'} : null}>
                  <td><input type="checkbox" readOnly /></td>
                  <td><Discovered lock>{f.name}</Discovered></td>
                  <td><Discovered>{f.sqlType}</Discovered></td>
                  <td><Discovered>{f.type}</Discovered></td>
                  <td><input readOnly className="inp" style={{height:20, padding:'0 6px', fontSize:10.5}} value={f.title} /></td>
                  <td><input readOnly className="inp" style={{height:20, padding:'0 6px', fontSize:10.5}} value={f.alias || '(use title)'} /></td>
                  <td><Discovered>{f.nullable ? 'nullable' : 'not null'}</Discovered></td>
                  <td><input type="checkbox" readOnly defaultChecked={f.editable} /></td>
                  <td>{f.length != null ? <Discovered>{f.length}</Discovered> : <span className="muted">—</span>}</td>
                  <td><input readOnly className="inp inp--mono" style={{height:20, padding:'0 6px', fontSize:10}} value={f.defaultV || ''} /></td>
                  <td>
                    {f.domain
                      ? <span className="tag" style={{cursor:'pointer'}}>{f.domain}</span>
                      : <a className="fs-override" style={{fontSize:10}}>+ Domain</a>}
                  </td>
                  <td>
                    {f.roles.length > 0
                      ? f.roles.map(r => <span key={r} className="tag" style={{marginRight:2, color:'var(--pencil)'}}>{r}</span>)
                      : <a className="fs-override" style={{fontSize:10}}>+ Role</a>}
                  </td>
                  <td className="muted" style={{fontSize:10}}>⋯</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* footer · issues */}
        <div style={{padding:'8px 14px', borderTop:'1.2px solid #e7c97a', background:'#fff7e6', display:'flex',alignItems:'center', gap:10, fontSize:11.5}}>
          <Badge kind="warn">2 issues</Badge>
          <span style={{flex:1}}>
            <b>geom</b> — 2 records missing CRS · blocks publish to OGC Features.
            <span className="muted" style={{marginLeft:8}}>· <b>owner_name</b> — marked sensitive, hidden by default in every layer.</span>
          </span>
          <Btn sm>View affected rows</Btn>
          <Btn kind="p" sm>Auto-fix CRS</Btn>
        </div>
      </div>
    </div>
  );
}

function FieldStateGuide() {
  // A teaching artboard — gives the operator one place to learn the vocabulary.
  return (
    <div className="scr scr--noside">
      <TopBar crumbs={['Field-state vocabulary']} />
      <div style={{padding:'14px 22px', overflow:'auto', height:'100%'}}>
        <h1 style={{margin:'0 0 4px', font:'600 18px var(--ui)'}}>Field-state vocabulary</h1>
        <div className="muted" style={{fontSize:11.5, marginBottom:14}}>
          Every field on every form belongs to one of five states. Learn them once, recognise them everywhere.
        </div>

        <div style={{display:'grid', gridTemplateColumns:'1.2fr 1fr', gap:18}}>
          {/* LEFT — the five states */}
          <div className="col" style={{gap:10}}>
            {[
              { ico:'✏️', name:'Input',      cls:'fs-input',      title:'You type or pick it',
                hint:'A blank canvas the operator fills in.',
                example: <Inp value="Tax Parcels (FY 2024)" /> },
              { ico:'🔍', name:'Discovered', cls:'fs-discovered', title:'Auto-filled, you can override',
                hint:'Honua read it from your data. You can change it and your override sticks across re-introspections.',
                example: <>
                  <input readOnly className="inp" style={{border:'1px dashed #c4d8b6', background:'#fafdf6', flex:1}} value="EPSG:4326" />
                  <span className="fs-override">Override</span>
                  <span className="fs-revert">↶</span>
                </> },
              { ico:'🧮', name:'Calculated', cls:'fs-calculated', title:'Derived from other fields',
                hint:'Read-only. Change the inputs and this updates.',
                example: <span className="mono" style={{fontSize:11, color:'#555'}}>http://opengis.net/def/crs/EPSG/0/4326 <span style={{color:'#888'}}>→ from SRID</span></span> },
              { ico:'⚙️', name:'System',     cls:'fs-system',     title:'System-assigned',
                hint:"Honua picked it. You don't touch it; the API exposes it for reference.",
                example: <span className="mono" style={{fontSize:10.5, color:'#999'}}>sb_01HZX7P4MN3R5K9Q84A</span> },
              { ico:'🔒', name:'Admin',      cls:'fs-admin',      title:'Server / admin config',
                hint:'Set in appsettings.json or env vars at deploy time. Surfaced here for transparency.',
                example: <span style={{fontStyle:'italic', color:'#888', fontSize:11}}>HONUA_LIMITS_MAX_UPLOAD_BYTES = 104857600</span> },
            ].map(s => (
              <div key={s.name} className="card" style={{padding:0}}>
                <div style={{padding:'8px 12px', borderBottom:'1px solid #e4e4e4', display:'flex',alignItems:'center', gap:8, background:'#fafafa'}}>
                  <span style={{fontSize:14}}>{s.ico}</span>
                  <b style={{fontSize:12}}>{s.name}</b>
                  <span className="muted" style={{fontSize:11}}>· {s.title}</span>
                </div>
                <div style={{padding:'10px 12px'}}>
                  <div style={{fontSize:11.5, color:'#555', marginBottom:8}}>{s.hint}</div>
                  <div className={'fs-row ' + s.cls} style={{borderBottom:'none', padding:0, gridTemplateColumns:'1fr'}}>
                    <div className="fs-value" style={{gap:6}}>{s.example}</div>
                  </div>
                </div>
              </div>
            ))}
          </div>

          {/* RIGHT — scope chips + worked sharing example */}
          <div className="col" style={{gap:10}}>
            <div className="card">
              <h3>Edit scope — who else gets affected</h3>
              <div className="muted" style={{fontSize:11, marginBottom:8}}>
                Every field group is also tagged with <b>scope</b>: which entities your edit will affect.
              </div>
              <div className="col" style={{gap:8, fontSize:11.5}}>
                <div className="row" style={{gap:8}}>
                  <ScopeChip scope="resource" count="3 layers across 2 services" />
                  <span className="muted" style={{flex:1}}>Resource metadata, fields, spatial, temporal, presentation, access defaults.</span>
                </div>
                <div className="row" style={{gap:8}}>
                  <ScopeChip scope="publication" count="this layer only" />
                  <span className="muted" style={{flex:1}}>Layer identifier, layer-name alias, title override.</span>
                </div>
                <div className="row" style={{gap:8}}>
                  <ScopeChip scope="service" count="8 layers in this service" />
                  <span className="muted" style={{flex:1}}>Service CRS, capabilities, output formats, rate limits, cache TTL.</span>
                </div>
                <div className="row" style={{gap:8}}>
                  <ScopeChip scope="server" />
                  <span className="muted" style={{flex:1}}>Admin/config-time. Goes in appsettings or env. Linked from Settings.</span>
                </div>
              </div>
            </div>

            <div className="card">
              <h3>Worked example · editing a shared resource field</h3>
              <div style={{fontSize:11.5, lineHeight:1.55, marginBottom:8}}>
                You change <span className="mono">Description</span> on <span className="mono">parcels_2024</span>.
              </div>
              <ol style={{margin:'0 0 0 18px', padding:0, fontSize:11, lineHeight:1.7}}>
                <li>UI shows <ScopeChip scope="resource" count="3 layers" /> next to the field group.</li>
                <li>Save shows a confirm: <b>"Affects 3 layers — PublicWorks/Roads/0, PublicWorks/Hydrants/0, OGC/parcels"</b>.</li>
                <li>Confirm → propagates to every binding layer immediately.</li>
                <li>Layer pages get a passive "updated 2m ago by jamie" hint with link back to the resource.</li>
              </ol>
            </div>

            <div className="card">
              <h3>Worked example · editing a 🔍 discovered value</h3>
              <ol style={{margin:'0 0 0 18px', padding:0, fontSize:11, lineHeight:1.7}}>
                <li>Discovered field has a dashed border and an <span className="fs-override">Override</span> link.</li>
                <li>Click Override → input becomes editable; value gets tagged "operator override".</li>
                <li>Click <span className="fs-revert">↶ revert</span> any time → snaps back to the discovered value and drops the override flag.</li>
                <li>Re-introspect → discovered fields refresh; <b>your overrides hold</b>; UI shows a Diff link if discovered ≠ your override.</li>
              </ol>
            </div>

            <Callout kind="info">
              <b>Why this matters.</b> A typical resource has 50+ fields across 10+ groups. Without a shared vocabulary, every form looks like a wall of inputs. With one, an operator can scan and answer "what do I have to fill in?" in seconds.
            </Callout>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ResSourceHi, ResFieldsHi, FieldStateGuide });
