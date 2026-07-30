import { useControlState } from '../hooks/useControlState';

const C = 207; // ring circumference

const MODE_ICONS = {
  silent: 'M11 5 6 9H2v6h4l5 4V5ZM16 9a4 4 0 0 1 0 6',
  office: 'M4 12h16M4 6h16M4 18h16',
  beast:  'M12 2c1 4 4 5 4 9a4 4 0 0 1-8 0c0-2 1-3 1-3s3 1 3-6Z',
  gaming: 'M12 2 4 6v6c0 5 3.5 8 8 10 4.5-2 8-5 8-10V6l-8-4Z',
};

const MODE_DESCS = {
  silent: '\u4f4e\u566a\u8282\u80fd',
  office: '\u65e5\u5e38\u63a8\u8350',
  beast:  '\u9ad8\u6027\u80fd',
  gaming: '\u6ee1\u8840\u91ca\u653e',
};

function tc(t) { return t >= 85 ? 't-danger' : t >= 65 ? 't-warn' : 't-ok'; }

export default function Dashboard() {
  const { telemetry, settings, profiles, platformInfo, switchProfile } = useControlState();
  const s = telemetry;
  const isBellator = platformInfo.oem === 'Bellator';

  // Filter to built-in profiles only for the dock
  const builtinProfiles = profiles.filter(p => p.builtIn);

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>{'\u4eea\u8868\u76d8'}</h1>
          <p>{'\u786c\u4ef6\u5b9e\u65f6\u76d1\u63a7 \u00b7 \u6570\u636e\u6bcf 250ms \u63a8\u9001 \u00b7 \u5361\u7247\u53ea\u8bfb'}</p>
        </div>
      </div>

      {/* Config switch dock - only on Bellator */}
      {isBellator && builtinProfiles.length > 0 && (
        <div className="dock card reveal enter" style={{ animationDelay: '.02s' }}>
          <span className="label">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" width="17" height="17"><path d="M13 2 3 14h7l-1 8 10-12h-7l1-8Z"/></svg>
            {'\u5f53\u524d\u914d\u7f6e'}
          </span>
          <div className="modes">
            {builtinProfiles.map(p => (
              <button
                key={p.id}
                className={'mode-btn' + (settings.mode === p.id ? ' active' : '')}
                onClick={() => switchProfile(p.id)}
              >
                <span className="ico">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" width="17" height="17">
                    <path d={MODE_ICONS[p.id] || MODE_ICONS.office} />
                  </svg>
                </span>
                <span className="txt">
                  <b>{p.name}</b>
                  <small>{MODE_DESCS[p.id] || ''}</small>
                </span>
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Sensor grid */}
      <div className="grid sensors">
        {/* CPU */}
        <div className="card sensor reveal enter" style={{ animationDelay: '.08s' }}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" width="17" height="17"><rect x="6" y="6" width="12" height="12" rx="1.5"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/></svg></span>CPU</span>
            <span className="live"><span className="d"></span>{'\u5b9e\u65f6'}</span>
          </div>
          <div className="ring-wrap">
            <div className="ring">
              <svg width="78" height="78"><circle cx="39" cy="39" r="33" fill="none" stroke="var(--stroke)" strokeWidth="7"/><circle cx="39" cy="39" r="33" fill="none" stroke="var(--primary)" strokeWidth="7" strokeLinecap="round" strokeDasharray={C} strokeDashoffset={C * (1 - (s.cpuUsage ?? 0) / 100)} style={{ transition: 'stroke-dashoffset .2s ease-out' }} /></svg>
              <span className="val">{Math.round(s.cpuUsage ?? 0)}<small>%</small></span>
            </div>
            <div className="meta">
              <div className="metric-row" style={{ border: 0, paddingTop: 0, marginTop: 0 }}><span className="k">{'\u6e29\u5ea6'}</span><span className={'v ' + tc(s.cpuTemp ?? 0)}>{Math.round(s.cpuTemp ?? 0)}{'\u00b0C'}</span></div>
              <div className="metric-row"><span className="k">{'\u9891\u7387'}</span><span className="v">{(s.cpuFreq ?? 0).toFixed(1)} GHz</span></div>
              <div className="metric-row"><span className="k">{'\u529f\u8017'}</span><span className="v">{Math.round(s.gpuPowerDrawW ?? 0)} W</span></div>
            </div>
          </div>
        </div>

        {/* GPU */}
        <div className="card sensor reveal enter" style={{ animationDelay: '.14s' }}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" width="17" height="17"><rect x="2" y="6" width="20" height="12" rx="1.5"/><circle cx="9" cy="12" r="3"/><path d="M16 10h3M16 14h3"/></svg></span>GPU</span>
            <span className="live"><span className="d"></span>{'\u5b9e\u65f6'}</span>
          </div>
          <div className="ring-wrap">
            <div className="ring">
              <svg width="78" height="78"><circle cx="39" cy="39" r="33" fill="none" stroke="var(--stroke)" strokeWidth="7"/><circle cx="39" cy="39" r="33" fill="none" stroke="var(--accent)" strokeWidth="7" strokeLinecap="round" strokeDasharray={C} strokeDashoffset={C * (1 - (s.gpuUsage ?? 0) / 100)} style={{ transition: 'stroke-dashoffset .2s ease-out' }} /></svg>
              <span className="val">{Math.round(s.gpuUsage ?? 0)}<small>%</small></span>
            </div>
            <div className="meta">
              <div className="metric-row" style={{ border: 0, paddingTop: 0, marginTop: 0 }}><span className="k">{'\u6e29\u5ea6'}</span><span className={'v ' + tc(s.gpuTemp ?? 0)}>{Math.round(s.gpuTemp ?? 0)}{'\u00b0C'}</span></div>
              <div className="metric-row"><span className="k">{'\u663e\u5b58'}</span><span className="v">{(s.gpuVramUsed ?? 0).toFixed(1)} GB</span></div>
              <div className="metric-row"><span className="k">{'\u9891\u7387'}</span><span className="v">{(s.gpuFreq ?? 0).toFixed(1)} GHz</span></div>
            </div>
          </div>
        </div>

        {/* Memory + Disk */}
        <div className="card sensor reveal enter" style={{ animationDelay: '.2s' }}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" width="17" height="17"><rect x="3" y="8" width="18" height="8" rx="1"/><path d="M7 8V6M12 8V6M17 8V6M7 16v2M12 16v2M17 16v2"/></svg></span>{'\u5185\u5b58 \u00b7 \u786c\u76d8'}</span>
            <span className="live"><span className="d"></span>{'\u5b9e\u65f6'}</span>
          </div>
          <div style={{ paddingTop: 4 }}>
            <div className="metric-row" style={{ border: 0, paddingTop: 0, marginTop: 2 }}><span className="k">{'\u5185\u5b58\u5360\u7528'}</span><span className="v">{(s.memoryTotalGB ?? 32) * (s.memoryUsage ?? 0) / 100 | 0}.{(s.memoryTotalGB ?? 32) * (s.memoryUsage ?? 0) / 100 % 1 * 10 | 0} / {s.memoryTotalGB ?? 32} GB</span></div>
            <div className="bar" style={{ margin: '8px 0 18px' }}><i style={{ width: (s.memoryUsage ?? 0) + '%' }}></i></div>
            <div className="metric-row" style={{ border: 0, paddingTop: 0, marginTop: 0 }}><span className="k">{'\u786c\u76d8\u5360\u7528'}</span><span className="v">{(s.diskTotalGB ?? 1024) * (s.diskUsage ?? 0) / 100 | 0}.{(s.diskTotalGB ?? 1024) * (s.diskUsage ?? 0) / 100 % 1 * 10 | 0} / {s.diskTotalGB ?? 1024} GB</span></div>
            <div className="bar" style={{ marginTop: '8px' }}><i style={{ width: (s.diskUsage ?? 0) + '%', background: 'linear-gradient(90deg,var(--accent),var(--primary))' }}></i></div>
          </div>
        </div>
      </div>

      {/* Fan info */}
      <div className="card fan-card reveal enter" style={{ animationDelay: '.26s' }}>
        <div className="head">
          <span className="t"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" width="17" height="17"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg></span>{'\u98ce\u6247\u4fe1\u606f'}</span>
          <span style={{ fontSize: '11.5px', color: 'var(--fg-3)' }}>{'EC \u5bc4\u5b58\u5668\u8bfb\u53d6 \u00b7 \u53ea\u8bfb'}</span>
        </div>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" width="18" height="18"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>{'\u5927\u98ce\u6247'}</span>
          <div className="bar"><i style={{ width: Math.min(100, Math.round((s.fanLargeRpm ?? 0) / ((s.fanLargeMax ?? 4400) || 1) * 100)) + '%' }}></i></div>
          <span className="rpm"><b>{Math.round(s.fanLargeRpm ?? 0)}</b> RPM<small>{'EC \u76f4\u8bfb'}</small></span>
        </div>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" width="18" height="18"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>{'\u5c0f\u98ce\u6247'}</span>
          <div className="bar"><i style={{ width: Math.min(100, Math.round((s.fanSmallRpm ?? 0) / ((s.fanSmallMax ?? 8200) || 1) * 100)) + '%' }}></i></div>
          <span className="rpm"><b>{Math.round(s.fanSmallRpm ?? 0)}</b> RPM<small>{'EC \u76f4\u8bfb'}</small></span>
        </div>
      </div>
    </section>
  );
}
