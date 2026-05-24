// Field-state vocabulary primitives + scope chips.
// Used across every detail screen so operators learn the visual language once.

// FieldRow — a single labelled field with state semantics.
//   state: 'input' | 'discovered' | 'calculated' | 'system' | 'admin'
//   label, hint, value, derivedFrom (for calculated), affects (for scope)
function FieldRow({ state = 'input', icon, label, sub, value, mono, derivedFrom, hint, override, revertable, children, meta }) {
  const STATE_MAP = {
    input:      { ico: '✏️', cls: 'fs-input',      pill: 'input',     pillLabel: 'input' },
    discovered: { ico: '🔍', cls: 'fs-discovered', pill: 'discover',  pillLabel: 'auto' },
    calculated: { ico: '🧮', cls: 'fs-calculated', pill: 'calc',      pillLabel: 'derived' },
    system:     { ico: '⚙️', cls: 'fs-system',     pill: 'system',    pillLabel: 'system' },
    admin:      { ico: '🔒', cls: 'fs-admin',      pill: 'admin',     pillLabel: 'admin only' },
  };
  const s = STATE_MAP[state] || STATE_MAP.input;
  return (
    <div className={'fs-row ' + s.cls}>
      <div className="fs-icon" title={s.pillLabel}>{icon || s.ico}</div>
      <div className="fs-label">
        {label}
        {sub && <div className="fs-sub">{sub}</div>}
      </div>
      <div className="fs-value">
        {children || (
          state === 'calculated' ? (
            <>
              <span>{value}</span>
              {derivedFrom && <span style={{ color: '#888', fontFamily: 'var(--ui)', fontSize: 10 }}>→ from <span style={{ color: 'var(--pencil)' }}>{derivedFrom}</span></span>}
            </>
          ) : state === 'system' ? (
            <span>{value}</span>
          ) : state === 'admin' ? (
            <span>{value}</span>
          ) : (
            <input readOnly className={'inp' + (mono ? ' inp--mono' : '')} style={{ width: '100%' }} value={value || ''} />
          )
        )}
        {state === 'discovered' && override && (
          <span className="fs-override" title="override the discovered value">Override</span>
        )}
        {state === 'discovered' && revertable && (
          <span className="fs-revert" title="revert to discovered value">↶ revert</span>
        )}
      </div>
      <div className="fs-meta">
        {hint && <div>{hint}</div>}
        {meta}
      </div>
    </div>
  );
}

// ScopeChip — communicates which entities an edit will affect.
//   scope: 'resource' | 'publication' | 'service' | 'server'
function ScopeChip({ scope = 'resource', children, count, items }) {
  const SCOPE_MAP = {
    resource:    { ico: '🌐', label: 'Resource-wide' },
    publication: { ico: '🔧', label: 'Layer-only' },
    service:     { ico: '⚙️', label: 'Service-wide' },
    server:      { ico: '🖥', label: 'Server-wide' },
  };
  const s = SCOPE_MAP[scope] || SCOPE_MAP.resource;
  return (
    <span className={'scope-chip scope-chip--' + scope}>
      <span className="scope-ico">{s.ico}</span>
      <b>{s.label}</b>
      {count != null && <span>· affects {count}</span>}
      {children && <span>· {children}</span>}
    </span>
  );
}

// FieldGroup — wraps a set of FieldRow with a header + scope chip + collapse.
function FieldGroup({ title, sub, scope, count, items, children, defaultOpen = true }) {
  return (
    <div className="card" style={{ padding: 0, marginBottom: 12 }}>
      <div style={{ padding: '8px 12px', borderBottom: '1px solid #e4e4e4', display: 'flex', alignItems: 'center', gap: 10, background: '#fafafa' }}>
        <div style={{ flex: 1 }}>
          <h3 style={{ margin: 0 }}>{title}</h3>
          {sub && <div className="muted" style={{ fontSize: 10.5, marginTop: 2 }}>{sub}</div>}
        </div>
        {scope && <ScopeChip scope={scope} count={count} items={items} />}
      </div>
      <div style={{ padding: '4px 12px' }}>
        {children}
      </div>
    </div>
  );
}

// Legend bar — appears once per page as a teaching aid.
function FieldStateLegend({ collapsible = true }) {
  return (
    <div style={{
      padding: '6px 12px',
      background: '#fafafa',
      borderTop: '1px dashed #d8d8d8',
      borderBottom: '1px dashed #d8d8d8',
      fontSize: 10.5,
      color: '#666',
      display: 'flex',
      alignItems: 'center',
      gap: 14,
      flexWrap: 'wrap',
    }}>
      <span className="muted" style={{ textTransform: 'uppercase', fontSize: 9.5, letterSpacing: '0.06em' }}>Field states</span>
      <span>✏️ you input</span>
      <span style={{ color: '#1d6b3e' }}>🔍 auto-discovered · overridable</span>
      <span style={{ color: '#1f3f87' }}>🧮 calculated from other fields</span>
      <span style={{ color: '#888' }}>⚙️ system-assigned</span>
      <span style={{ color: '#8a5a04' }}>🔒 admin / server config</span>
      {collapsible && <span style={{ marginLeft: 'auto', color: '#999', cursor: 'pointer' }}>hide ▴</span>}
    </div>
  );
}

Object.assign(window, { FieldRow, ScopeChip, FieldGroup, FieldStateLegend });
