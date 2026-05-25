// Shared map preview component. Two modes:
//   mode="layer"   — just the selected layer's features, with style+labels+popup
//   mode="service" — composite of all layers in the service, smaller chrome,
//                    with a layer list overlay to toggle visibility.

function MapPreview({ mode = 'layer', height = 360, popup = true, scaleText = '1:8,000' }) {
  const isService = mode === 'service';
  return (
    <div style={{
      position: 'relative',
      border: '1px solid var(--ink)',
      borderRadius: 6,
      overflow: 'hidden',
      background: '#eef3ee',
      height,
    }}>
      <svg
        viewBox="0 0 800 420"
        preserveAspectRatio="xMidYMid slice"
        style={{ display: 'block', width: '100%', height: '100%' }}
      >
        <defs>
          <pattern id="mp-basegrid" width="40" height="40" patternUnits="userSpaceOnUse">
            <path d="M 40 0 L 0 0 0 40" fill="none" stroke="#dde6dd" strokeWidth="1" />
          </pattern>
          <linearGradient id="mp-basesheen" x1="0" x2="1">
            <stop offset="0%" stopColor="#f0f5ef" />
            <stop offset="100%" stopColor="#e3ebe2" />
          </linearGradient>
          <pattern id="mp-wet" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
            <rect width="6" height="6" fill="#b8d4dc" />
            <line x1="0" y1="3" x2="6" y2="3" stroke="#7ca7b3" strokeWidth="0.8" />
          </pattern>
        </defs>
        <rect width="800" height="420" fill="url(#mp-basesheen)" />
        <rect width="800" height="420" fill="url(#mp-basegrid)" />

        {/* coastline */}
        <path d="M 0 280 C 80 220, 160 320, 260 240 S 460 320, 600 220 S 780 180, 820 240 L 820 420 L 0 420 Z" fill="#c9d8c5" opacity="0.7" />

        {/* SERVICE-only layers behind parcels */}
        {isService && (
          <g>
            {/* wetlands */}
            <path d="M 540 240 C 580 220, 640 250, 660 280 S 600 330, 540 310 Z" fill="url(#mp-wet)" stroke="#5e8997" strokeWidth="0.7" />
            <path d="M 40 290 C 70 270, 110 280, 130 305 S 100 340, 50 330 Z" fill="url(#mp-wet)" stroke="#5e8997" strokeWidth="0.7" />
          </g>
        )}

        {/* roads (service mode draws all; layer mode shows faint reference roads only) */}
        <g stroke={isService ? '#7a7a7a' : '#bbb'} strokeWidth={isService ? 1.6 : 1} fill="none">
          <path d="M 0 200 L 800 220" />
          <path d="M 0 320 L 800 300" />
          <path d="M 400 0 L 410 420" />
        </g>

        {/* PARCEL POLYGONS — main layer in both modes */}
        {[
          ['#f7f4e8', 60, 60, 60, 40],
          ['#ead78a', 120, 60, 70, 40],
          ['#ead78a', 190, 60, 60, 40],
          ['#d9a23a', 60, 100, 100, 50],
          ['#b56b1c', 160, 100, 50, 50],
          ['#612d0a', 210, 100, 80, 50],
          ['#f7f4e8', 290, 60, 90, 80],
          ['#ead78a', 380, 60, 60, 50],
          ['#d9a23a', 380, 110, 60, 30],
          ['#612d0a', 440, 60, 90, 80],
          ['#b56b1c', 530, 60, 70, 50],
          ['#ead78a', 530, 110, 70, 30],
          ['#d9a23a', 600, 60, 100, 80],
          ['#612d0a', 700, 60, 60, 40],
          ['#b56b1c', 700, 100, 60, 40],
          ['#f7f4e8', 60, 150, 80, 60],
          ['#ead78a', 140, 150, 60, 60],
          ['#d9a23a', 200, 150, 80, 60],
          ['#b56b1c', 280, 150, 100, 60],
          ['#ead78a', 380, 150, 70, 60],
          ['#d9a23a', 450, 150, 80, 60],
          ['#f7f4e8', 530, 150, 90, 60],
          ['#612d0a', 620, 150, 80, 60],
          ['#b56b1c', 700, 150, 60, 60],
        ].map((p, i) => (
          <rect key={i} x={p[1]} y={p[2]} width={p[3]} height={p[4]} fill={p[0]} stroke="#7a6f55" strokeWidth="0.7" />
        ))}

        {/* highlighted parcel (layer mode) */}
        {mode === 'layer' && popup && (
          <rect x="200" y="150" width="80" height="60" fill="none" stroke="#141414" strokeWidth="2" />
        )}

        {/* SERVICE-mode point layers on top */}
        {isService && (
          <g>
            {/* hydrants */}
            {[[120,170],[230,90],[360,180],[480,120],[560,180],[670,100],[750,180],[90,260],[300,260],[510,260],[660,260]].map((p,i) => (
              <g key={'h'+i}>
                <circle cx={p[0]} cy={p[1]} r="3.5" fill="#c84a30" stroke="#fff" strokeWidth="0.8" />
              </g>
            ))}
            {/* observation sites */}
            {[[150,300],[400,280],[620,310],[720,340]].map((p,i) => (
              <rect key={'o'+i} x={p[0]-3} y={p[1]-3} width="6" height="6" fill="#2a6fdb" stroke="#fff" strokeWidth="0.7" />
            ))}
          </g>
        )}

        {/* labels (layer mode at this scale) */}
        {mode === 'layer' && (
          <g fontFamily="Inter, system-ui, sans-serif" fontSize="9" fill="#3a2f17">
            <text x="92" y="84" textAnchor="middle">04-021-118</text>
            <text x="155" y="84" textAnchor="middle">04-021-119</text>
            <text x="240" y="125" textAnchor="middle">04-021-122</text>
            <text x="240" y="184" textAnchor="middle" style={{ fontWeight: 600 }}>04-021-204</text>
            <text x="585" y="184" textAnchor="middle">04-021-211</text>
          </g>
        )}

        {/* north arrow */}
        <g transform="translate(750, 40)">
          <circle r="14" fill="#fff" stroke="#141414" />
          <path d="M 0 -10 L 4 8 L 0 4 L -4 8 z" fill="#141414" />
          <text y="-18" textAnchor="middle" fontSize="9" fontFamily="Inter">N</text>
        </g>
      </svg>

      {/* zoom chrome */}
      <div style={{ position: 'absolute', top: 10, left: 10, display: 'flex', flexDirection: 'column', gap: 4 }}>
        <div style={{ width: 26, height: 26, background: '#fff', border: '1px solid #141414', borderRadius: 4, display: 'grid', placeItems: 'center', fontWeight: 700 }}>+</div>
        <div style={{ width: 26, height: 26, background: '#fff', border: '1px solid #141414', borderRadius: 4, display: 'grid', placeItems: 'center', fontWeight: 700 }}>−</div>
        <div style={{ width: 26, height: 26, background: '#fff', border: '1px solid #141414', borderRadius: 4, display: 'grid', placeItems: 'center' }}>⌖</div>
      </div>

      {/* scale */}
      <div style={{
        position: 'absolute', bottom: 10, left: 10,
        background: 'rgba(255,255,255,0.92)', border: '1px solid #141414', borderRadius: 4,
        padding: '4px 8px', fontSize: 10, fontFamily: 'var(--mono)',
      }}>
        <div className="row" style={{ gap: 8 }}>
          <span>{scaleText}</span>
          <span style={{ color: '#888' }}>·</span>
          <span>EPSG:4326</span>
          <span style={{ color: '#888' }}>·</span>
          <span>z 14</span>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 10, right: 10, background: 'rgba(255,255,255,0.92)', border: '1px solid #141414', borderRadius: 4, padding: '4px 8px', fontSize: 10 }}>
        100m
        <div style={{ width: 60, height: 3, background: '#141414', marginTop: 2 }} />
      </div>

      {/* SERVICE mode layer list overlay */}
      {isService && (
        <div style={{
          position: 'absolute', top: 10, right: 10, width: 180,
          background: 'rgba(255,255,255,0.96)', border: '1px solid #141414', borderRadius: 4,
          fontSize: 10.5,
        }}>
          <div style={{ padding: '5px 8px', borderBottom: '1px solid #eee', fontWeight: 600 }}>
            Layers · 8
          </div>
          {[
            { id: 0, n: 'Parcels',         c: '#d9a23a', sh: 'rect', on: true },
            { id: 1, n: 'Road centerlines',c: '#7a7a7a', sh: 'line', on: true },
            { id: 2, n: 'Hydrants',        c: '#c84a30', sh: 'circle', on: true },
            { id: 3, n: 'Wetlands',        c: '#7ca7b3', sh: 'patt', on: true },
            { id: 4, n: 'Fire perimeters', c: '#aa3a2b', sh: 'rect', on: false },
            { id: 5, n: 'Watersheds',      c: '#4a8b6f', sh: 'rect', on: false },
            { id: 6, n: 'Observation sites', c: '#2a6fdb', sh: 'square', on: true },
            { id: 7, n: 'Fire observations', c: '#888',  sh: 'square', on: false },
          ].map(l => (
            <div key={l.id} className="row" style={{ padding: '3px 8px', borderBottom: '1px solid #f4f4f4', gap: 6 }}>
              <input type="checkbox" readOnly defaultChecked={l.on} style={{ margin: 0 }} />
              <span style={{
                width: 12, height: 12, display: 'inline-block',
                background: l.sh === 'patt' ? 'url(#mp-wet)' : l.c,
                borderRadius: l.sh === 'circle' ? '50%' : 0,
                border: l.sh === 'line' ? 'none' : '1px solid #555',
                borderTop: l.sh === 'line' ? '2px solid ' + l.c : '1px solid #555',
              }} />
              <span className="mono" style={{ fontSize: 9, color: '#888', width: 12 }}>{l.id}</span>
              <span style={{ flex: 1, opacity: l.on ? 1 : 0.5 }}>{l.n}</span>
            </div>
          ))}
        </div>
      )}

      {/* popup on selected feature (layer mode) */}
      {mode === 'layer' && popup && (
        <div style={{
          position: 'absolute', left: '30%', top: '45%',
          width: 220, background: '#fff', border: '1.2px solid #141414', borderRadius: 6,
          boxShadow: '0 4px 12px rgba(0,0,0,.18)',
          fontSize: 11,
        }}>
          <div style={{ padding: '6px 10px', borderBottom: '1px solid #eee', fontWeight: 600, background: '#fffae0' }}>
            Parcel 04-021-204 · 2,008 m²
          </div>
          <div style={{ padding: '6px 10px' }}>
            <div className="row"><span className="muted" style={{ flex: 1 }}>Use</span><span>Single-family</span></div>
            <div className="row"><span className="muted" style={{ flex: 1 }}>Assessed</span><span className="mono">$582,000</span></div>
            <div className="row"><span className="muted" style={{ flex: 1 }}>Last assessed</span><span>2024-08-12</span></div>
            <div className="row"><span className="muted" style={{ flex: 1 }}>Owner</span><span className="muted">— redacted —</span></div>
          </div>
          <div style={{ padding: '4px 10px', borderTop: '1px dashed #eee', fontSize: 10, color: '#888' }}>
            GetFeatureInfo preview · 4 of 23 fields exposed
          </div>
        </div>
      )}
    </div>
  );
}

Object.assign(window, { MapPreview });
