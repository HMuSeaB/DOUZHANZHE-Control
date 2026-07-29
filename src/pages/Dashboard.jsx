import { useControlState } from "../hooks/useControlState";

const C = 207; // 环形周长

// 模式映射（useControlState 的 settings.mode 与显示名对应）
const MODE_MAP = {
  silent: { label: "安静", desc: "低噪节能", icon: "M11 5 6 9H2v6h4l5 4V5ZM16 9a4 4 0 0 1 0 6" },
  office: { label: "均衡", desc: "日常推荐", icon: "M4 12h16M4 6h16M4 18h16" },
  beast:  { label: "野兽", desc: "高性能",   icon: "M12 2c1 4 4 5 4 9a4 4 0 0 1-8 0c0-2 1-3 1-3s3 1 3-6Z" },
  gaming: { label: "斗战", desc: "满血释放", icon: "M12 2 4 6v6c0 5 3.5 8 8 10 4.5-2 8-5 8-10V6l-8-4Z" },
};

function tc(t) { return t >= 85 ? "t-danger" : t >= 65 ? "t-warn" : "t-ok"; }

export default function Dashboard() {
  const { telemetry, settings } = useControlState();
  const s = telemetry;

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>仪表盘</h1>
          <p>硬件实时监控 · 数据每 250ms 推送 · 卡片只读</p>
        </div>
      </div>

      {/* 配置切换 Dock */}
      <div className="dock card reveal enter" style={{animationDelay:".02s"}}>
        <span className="label">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M13 2 3 14h7l-1 8 10-12h-7l1-8Z"/></svg>
          当前配置
        </span>
        <div className="modes">
          {Object.entries(MODE_MAP).map(([key, m]) => (
            <button key={key} className={"mode-btn" + (settings.mode === key ? " active" : "")}>
              <span className="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d={m.icon}/></svg></span>
              <span className="txt"><b>{m.label}</b><small>{m.desc}</small></span>
            </button>
          ))}
        </div>
      </div>

      {/* 传感器网格 */}
      <div className="grid sensors">
        {/* CPU */}
        <div className="card sensor reveal enter" style={{animationDelay:".08s"}}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="6" y="6" width="12" height="12" rx="1.5"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/></svg></span>CPU</span>
            <span className="live"><span className="d"></span>实时</span>
          </div>
          <div className="ring-wrap">
            <div className="ring">
              <svg width="78" height="78"><circle cx="39" cy="39" r="33" fill="none" stroke="var(--stroke)" strokeWidth="7"/><circle cx="39" cy="39" r="33" fill="none" stroke="var(--primary)" strokeWidth="7" strokeLinecap="round" strokeDasharray={C} strokeDashoffset={C * (1 - (s.cpuUsage ?? 0) / 100)} style={{transition:"stroke-dashoffset .2s ease-out"}}/></svg>
              <span className="val">{Math.round(s.cpuUsage ?? 0)}<small>%</small></span>
            </div>
            <div className="meta">
              <div className="metric-row" style={{border:0,paddingTop:0,marginTop:0}}><span className="k">温度</span><span className={"v " + tc(s.cpuTemp ?? 0)}>{Math.round(s.cpuTemp ?? 0)}°C</span></div>
              <div className="metric-row"><span className="k">频率</span><span className="v">{(s.cpuFreq ?? 0).toFixed(1)} GHz</span></div>
              <div className="metric-row"><span className="k">功耗</span><span className="v">{Math.round(s.gpuPowerDrawW ?? 0)} W</span></div>
            </div>
          </div>
        </div>

        {/* GPU */}
        <div className="card sensor reveal enter" style={{animationDelay:".14s"}}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="2" y="6" width="20" height="12" rx="1.5"/><circle cx="9" cy="12" r="3"/><path d="M16 10h3M16 14h3"/></svg></span>GPU</span>
            <span className="live"><span className="d"></span>实时</span>
          </div>
          <div className="ring-wrap">
            <div className="ring">
              <svg width="78" height="78"><circle cx="39" cy="39" r="33" fill="none" stroke="var(--stroke)" strokeWidth="7"/><circle cx="39" cy="39" r="33" fill="none" stroke="var(--accent)" strokeWidth="7" strokeLinecap="round" strokeDasharray={C} strokeDashoffset={C * (1 - (s.gpuUsage ?? 0) / 100)} style={{transition:"stroke-dashoffset .2s ease-out"}}/></svg>
              <span className="val">{Math.round(s.gpuUsage ?? 0)}<small>%</small></span>
            </div>
            <div className="meta">
              <div className="metric-row" style={{border:0,paddingTop:0,marginTop:0}}><span className="k">温度</span><span className={"v " + tc(s.gpuTemp ?? 0)}>{Math.round(s.gpuTemp ?? 0)}°C</span></div>
              <div className="metric-row"><span className="k">显存</span><span className="v">{(s.gpuVramUsed ?? 0).toFixed(1)} GB</span></div>
              <div className="metric-row"><span className="k">频率</span><span className="v">{(s.gpuFreq ?? 0).toFixed(1)} GHz</span></div>
            </div>
          </div>
        </div>

        {/* 内存 + 硬盘 */}
        <div className="card sensor reveal enter" style={{animationDelay:".2s"}}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="8" width="18" height="8" rx="1"/><path d="M7 8V6M12 8V6M17 8V6M7 16v2M12 16v2M17 16v2"/></svg></span>内存 · 硬盘</span>
            <span className="live"><span className="d"></span>实时</span>
          </div>
          <div style={{paddingTop:4}}>
            <div className="metric-row" style={{border:0,paddingTop:0,marginTop:2}}><span className="k">内存占用</span><span className="v">{(s.memoryTotalGB ?? 32) * (s.memoryUsage ?? 0) / 100 | 0}.{(s.memoryTotalGB ?? 32) * (s.memoryUsage ?? 0) / 100 % 1 * 10 | 0} / {s.memoryTotalGB ?? 32} GB</span></div>
            <div className="bar" style={{margin:"8px 0 18px"}}><i style={{width: (s.memoryUsage ?? 0) + "%"}}></i></div>
            <div className="metric-row" style={{border:0,paddingTop:0,marginTop:0}}><span className="k">硬盘占用</span><span className="v">{(s.diskTotalGB ?? 1024) * (s.diskUsage ?? 0) / 100 | 0}.{(s.diskTotalGB ?? 1024) * (s.diskUsage ?? 0) / 100 % 1 * 10 | 0} / {s.diskTotalGB ?? 1024} GB</span></div>
            <div className="bar" style={{marginTop:"8px"}}><i style={{width: (s.diskUsage ?? 0) + "%",background:"linear-gradient(90deg,var(--accent),var(--primary))"}}></i></div>
          </div>
        </div>
      </div>

      {/* 风扇信息 */}
      <div className="card fan-card reveal enter" style={{animationDelay:".26s"}}>
        <div className="head">
          <span className="t"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg></span>风扇信息</span>
          <span style={{fontSize:"11.5px",color:"var(--fg-3)"}}>EC 寄存器读取 · 只读</span>
        </div>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>大风扇</span>
          <div className="bar"><i style={{width: Math.min(100, Math.round((s.fanLargeRpm ?? 0) / ((s.fanLargeMax ?? 4400) || 1) * 100)) + "%"}}></i></div>
          <span className="rpm"><b>{Math.round(s.fanLargeRpm ?? 0)}</b> RPM<small>EC 直读</small></span>
        </div>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>小风扇</span>
          <div className="bar"><i style={{width: Math.min(100, Math.round((s.fanSmallRpm ?? 0) / ((s.fanSmallMax ?? 8200) || 1) * 100)) + "%"}}></i></div>
          <span className="rpm"><b>{Math.round(s.fanSmallRpm ?? 0)}</b> RPM<small>EC 直读</small></span>
        </div>
      </div>
    </section>
  );
}
