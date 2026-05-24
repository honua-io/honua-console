// Shared shell components for all wireframe screens.

const NAV = [
  { grp: 'Operate', items: [
    { gl: '◷', t: 'Environments', k: 'environments', ct: '3' },
    { gl: '!',  t: 'Alerts', k: 'alerts', ct: '4' },
  ]},
  { grp: 'Overview', items: [
    { gl: '⌂', t: 'Dashboard', k: 'dashboard' },
  ]},
  { grp: 'Author', items: [
    { gl: '↗', t: 'Connections', k: 'connections', ct: '6' },
    { gl: '◇', t: 'Data Resources', k: 'resources', ct: '128' },
    { gl: '↓', t: 'Imports', k: 'imports', ct: '2' },
  ]},
  { grp: 'Publish', items: [
    { gl: '▤', t: 'Services & Layers', k: 'services', ct: '9' },
    { gl: '⌘', t: 'Catalogs', k: 'catalogs', ct: '5' },
    { gl: '⊞', t: 'Publishing', k: 'publishing' },
  ]},
  { grp: 'Operate', items: [
    { gl: '⚡', t: 'Activity', k: 'activity', ct: '3' },
    { gl: '◐', t: 'Validation', k: 'validation', ct: '6' },
  ]},
  { grp: 'Govern', items: [
    { gl: '⚿', t: 'Access', k: 'access' },
    { gl: '⚙', t: 'Settings', k: 'settings' },
  ]},
];

function Sidebar({ active }) {
  return (
    <aside className="side">
      <div style={{padding:'8px 12px 4px', display:'flex',alignItems:'center',gap:8}}>
        <div style={{
          width:22,height:22,border:'1.5px solid #141414',borderRadius:5,
          display:'grid',placeItems:'center',fontWeight:700,fontSize:11,background:'#ffe55c'
        }}>H</div>
        <div style={{fontWeight:700,fontSize:12,letterSpacing:'0.01em'}}>Honua Console</div>
      </div>
      <div style={{height:1, background:'#e4e4e4', margin:'6px 0'}} />
      {NAV.map((g, gi) => (
        <div key={gi}>
          <div className="grp">{g.grp}</div>
          {g.items.map(it => (
            <div key={it.k} className={'it' + (active === it.k ? ' on' : '')}>
              <span className="gl">{it.gl}</span>
              <span>{it.t}</span>
              {it.ct ? <span className="ct">{it.ct}</span> : null}
            </div>
          ))}
        </div>
      ))}
      <div style={{position:'absolute',bottom:0}} />
    </aside>
  );
}

function TopBar({ crumbs = [], right, env = 'dev', area = 'operate' }) {
  const ENVS = {
    dev:     { color:'#2a6fdb', label:'dev',     state:'healthy' },
    staging: { color:'#d97706', label:'staging', state:'healthy' },
    prod:    { color:'#1d6b3e', label:'prod',    state:'degraded · 1' },
  };
  const e = ENVS[env] || ENVS.dev;
  const AREAS = ['studio','catalog','operate','share'];
  return (
    <div className="topbar">
      <div className="brand">honua</div>
      {/* workspace · env pill */}
      <div style={{
        display:'inline-flex', alignItems:'center', gap:6,
        border:'1.2px solid var(--ink)', borderRadius:5,
        padding:'2px 4px 2px 8px', background:'#fff', cursor:'pointer',
      }} title="Workspace · environment">
        <span style={{fontSize:10.5, color:'#555'}}>Public Works</span>
        <span style={{color:'#bbb', fontSize:9}}>·</span>
        <span style={{width:7, height:7, borderRadius:'50%', background:e.color}} />
        <span style={{fontSize:11, fontWeight:600}}>{e.label}</span>
        <span className="muted" style={{fontSize:9.5}}>· {e.state}</span>
        <span style={{fontSize:9, color:'#888'}}>▾</span>
      </div>
      {/* workflow areas */}
      <div style={{display:'inline-flex', gap:0, marginLeft:8}}>
        {AREAS.map(a => (
          <div key={a} style={{
            padding:'4px 10px',
            fontSize:11.5,
            fontWeight: area === a ? 600 : 400,
            color: area === a ? 'var(--ink)' : '#888',
            borderBottom: area === a ? '2px solid var(--ink)' : '2px solid transparent',
            cursor:'pointer',
            textTransform:'capitalize',
          }}>{a}</div>
        ))}
      </div>
      <div className="crumbs" style={{marginLeft:8}}>
        {crumbs.map((c, i) => (
          <React.Fragment key={i}>
            {i > 0 && <span className="sep">/</span>}
            <span className={i === crumbs.length - 1 ? 'here' : ''}>{c}</span>
          </React.Fragment>
        ))}
      </div>
      <div className="spacer" />
      <div className="search">⌕ <span>Search content, resources, jobs…</span><span style={{marginLeft:'auto',color:'#bbb',fontFamily:'var(--mono)',fontSize:10}}>⌘K</span></div>
      {/* persistent indicators */}
      <div className="iconbtn" title="3 background jobs">⚡ 3</div>
      <div className="iconbtn" title="4 alerts" style={{background:'#fbeae7', borderColor:'#c03b2b', color:'#8a2218'}}>! 4</div>
      <div className="iconbtn">?</div>
      <div className="iconbtn" style={{background:'#ffe55c', borderColor:'#141414'}}>JD</div>
    </div>
  );
}

function PageHead({ title, sub, actions }) {
  return (
    <div className="pagehead">
      <div>
        <h1>{title}</h1>
        {sub && <div className="sub">{sub}</div>}
      </div>
      <div className="spacer" />
      <div className="row">{actions}</div>
    </div>
  );
}

function Badge({ kind = '', children, lg }) {
  const cls = 'bd' + (kind ? ' bd--' + kind : '') + (lg ? ' bd--lg' : '');
  return <span className={cls}><i className="dot" />{children}</span>;
}

function Btn({ kind = '', sm, children, ico }) {
  let cls = 'btn';
  if (kind === 'p') cls += ' btn--p';
  if (kind === 'a') cls += ' btn--a';
  if (kind === 'ghost') cls += ' btn--ghost';
  if (sm) cls += ' btn--sm';
  return <button className={cls}>{ico && <span>{ico}</span>}{children}</button>;
}

function FiltChip({ on, children, x }) {
  return (
    <span className={'filtchip' + (on ? ' filtchip--on' : '')}>
      {children}
      {x && <span className="x">×</span>}
    </span>
  );
}

function Toolbar({ filters, right }) {
  return (
    <div className="toolbar">
      {filters}
      <div style={{flex:1}} />
      {right}
    </div>
  );
}

function Tabs({ items, active, sub }) {
  return (
    <div className={'tabs' + (sub ? ' tabs--sub' : '')}>
      {items.map(it => (
        <div key={it.k} className={'tab' + (active === it.k ? ' on' : '')}>
          {it.t}{it.ct != null && <span className="ct">{it.ct}</span>}
        </div>
      ))}
    </div>
  );
}

function Stepper({ steps, on }) {
  return (
    <div className="stepper">
      {steps.map((s, i) => (
        <React.Fragment key={i}>
          {i > 0 && <span className="bar" />}
          <div className={'step ' + (i < on ? 'done' : i === on ? 'on' : '')}>
            <div className="n">{i < on ? '✓' : i + 1}</div>
            <div>{s}</div>
          </div>
        </React.Fragment>
      ))}
    </div>
  );
}

function Field({ label, hint, children, err }) {
  return (
    <div className="field">
      <label>{label}</label>
      {children}
      {hint && <div className={'hint' + (err ? ' err' : '')} style={err ? {color:'var(--redline)'} : null}>{hint}</div>}
    </div>
  );
}

function Inp({ value, placeholder, mono, err }) {
  return (
    <input
      readOnly
      className={'inp' + (mono ? ' inp--mono' : '') + (err ? ' err' : '')}
      value={value || ''}
      placeholder={placeholder}
    />
  );
}

function Sel({ value, placeholder }) {
  return (
    <div className="sel">
      <Inp value={value} placeholder={placeholder} />
    </div>
  );
}

function Ann({ red, children, style }) {
  return <div className={'ann' + (red ? ' ann--red' : '')} style={style}>{children}</div>;
}

function Callout({ kind, children }) {
  const cls = 'callout' + (kind ? ' callout--' + kind : '');
  return <div className={cls}>{children}</div>;
}

function Ph({ children, style, className }) {
  return <div className={'ph ' + (className || '')} style={style}>{children}</div>;
}

function Bar({ pct }) {
  return <div className="bar"><i style={{width: pct + '%'}} /></div>;
}

Object.assign(window, {
  Sidebar, TopBar, PageHead, Badge, Btn, FiltChip, Toolbar, Tabs, Stepper,
  Field, Inp, Sel, Ann, Callout, Ph, Bar, NAV,
});
