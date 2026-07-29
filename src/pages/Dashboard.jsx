import { useState, useEffect } from "react";

/* ---------- 模拟实时数据（后续替换为 WebSocket） ---------- */
function drift(v, min, max, step) {
  v += (Math.random() - 0.5) * step;
  return Math.max(min, Math.min(max, v));
}

const C = 207; // 环形周长

export default function Dashboard() {
  const [s, setS] = useState({
    cpu: 23, gpu: 45, cpuT: 67, gpuT: 72,
    cpuF: 3.2, gpuF: 1.6, cpuP: 28, gpuV: 4.2,
    fan1: 3200, fan2: 5400,
    mem: 12.4, disk: 1.0,
  });

  useEffect(() => {
    const t = setInterval(() => {
      setS(prev => ({
        cpu: drift(prev.cpu, 6, 96, 9),
        gpu: drift(prev.gpu, 4, 99, 11),
        cpuT: drift(prev.cpuT, 48, 92, 2.4),
        gpuT: drift(prev.gpuT, 45, 90, 2.8),
        cpuF: drift(prev.cpuF, 1.2, 4.8, 0.28),
        gpuF: drift(prev.gpuF, 0.6, 2.4, 0.18),
        cpuP: drift(prev.cpuP, 12, 90, 6),
        gpuV: drift(prev.gpuV, 1.5, 7.8, 0.4),
        fan1: drift(prev.fan1, 1800, 5200, 220),
        fan2: drift(prev.fan2, 2400, 6400, 260),
        mem: 12.4, disk: 1.0, // 固定值
      }));
    }, 250);
    return () => clearInterval(t);
  }, []);

  function tc(t) { return t >= 85 ? 't-danger' : t >= 65 ? 't-warn' : 't-ok'; }

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>仪表盘</h1>
          <p>硬件实时监控 · 数据每 250ms 推送 · 卡片只读</p>
        </div>
      </div>

      {/* 配置切换 Dock */}
      <div className="dock card reveal enter" style={{animationDelay:'.02s'}}>
        <span className="label">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M13 2 3 14h7l-1 8 10-12h-7l1-8Z"/></svg>
          当前配置
        </span>
        <div className="modes">
          {[
            { id:'安静', icon:'M11 5 6 9H2v6h4l5 4V5ZM16 9a4 4 0 0 1 0 6', desc:'低噪节能' },
            { id:'均衡', icon:'M4 12h16M4 6h16M4 18h16', desc:'日常推荐' },
            { id:'野兽', icon:'M12 2c1 4 4 5 4 9a4 4 0 0 1-8 0c0-2 1-3 1-3s3 1 3-6Z', desc:'高性能' },
            { id:'斗战', icon:'M12 2 4 6v6c0 5 3.5 8 8 10 4.5-2 8-5 8-10V6l-8-4Z', desc:'满血释放' },
          ].map(m => (
            <button key={m.id} className="mode-btn">
              <span className="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d={m.icon}/></svg></span>
              <span className="txt"><b>{m.id}</b><small>{m.desc}</small></span>
            </button>
          ))}
        </div>
      </div>

      {/* 传感器网格 */}
      <div className="grid sensors">
        {/* CPU */}
        <div className="card sensor reveal enter" style={{animationDelay:'.08s'}}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="6" y="6" width="12" height="12" rx="1.5"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/></svg></span>CPU</span>
            <span className="live"><span className="d"></span>实时</span>
          </div>
          <div className="ring-wrap">
            <div className="ring">
              <svg width="78" height="78"><circle cx="39" cy="39" r="33" fill="none" stroke="var(--stroke)" strokeWidth="7"/><circle cx="39" cy="39" r="33" fill="none" stroke="var(--primary)" strokeWidth="7" strokeLinecap="round" strokeDasharray={C} strokeDashoffset={C * (1 - s.cpu / 100)} style={{transition:'stroke-dashoffset .2s ease-out'}}/></svg>
              <span className="val">{Math.round(s.cpu)}<small>%</small></span>
            </div>
            <div className="meta">
              <div className="metric-row" style={{border:0,paddingTop:0,marginTop:0}}><span className="k">温度</span><span className={'v ' + tc(s.cpuT)}>{Math.round(s.cpuT)}°C</span></div>
              <div className="metric-row"><span className="k">频率</span><span className="v">{s.cpuF.toFixed(1)} GHz</span></div>
              <div className="metric-row"><span className="k">功耗</span><span className="v">{Math.round(s.cpuP)} W</span></div>
            </div>
          </div>
        </div>

        {/* GPU */}
        <div className="card sensor reveal enter" style={{animationDelay:'.14s'}}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="2" y="6" width="20" height="12" rx="1.5"/><circle cx="9" cy="12" r="3"/><path d="M16 10h3M16 14h3"/></svg></span>GPU</span>
            <span className="live"><span className="d"></span>实时</span>
          </div>
          <div className="ring-wrap">
            <div className="ring">
              <svg width="78" height="78"><circle cx="39" cy="39" r="33" fill="none" stroke="var(--stroke)" strokeWidth="7"/><circle cx="39" cy="39" r="33" fill="none" stroke="var(--accent)" strokeWidth="7" strokeLinecap="round" strokeDasharray={C} strokeDashoffset={C * (1 - s.gpu / 100)} style={{transition:'stroke-dashoffset .2s ease-out'}}/></svg>
              <span className="val">{Math.round(s.gpu)}<small>%</small></span>
            </div>
            <div className="meta">
              <div className="metric-row" style={{border:0,paddingTop:0,marginTop:0}}><span className="k">温度</span><span className={'v ' + tc(s.gpuT)}>{Math.round(s.gpuT)}°C</span></div>
              <div className="metric-row"><span className="k">显存</span><span className="v">{s.gpuV.toFixed(1)} GB</span></div>
              <div className="metric-row"><span className="k">频率</span><span className="v">{s.gpuF.toFixed(1)} GHz</span></div>
            </div>
          </div>
        </div>

        {/* 内存 + 硬盘 */}
        <div className="card sensor reveal enter" style={{animationDelay:'.2s'}}>
          <div className="top">
            <span className="name"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="8" width="18" height="8" rx="1"/><path d="M7 8V6M12 8V6M17 8V6M7 16v2M12 16v2M17 16v2"/></svg></span>内存 · 硬盘</span>
            <span className="live"><span className="d"></span>实时</span>
          </div>
          <div style={{paddingTop:4}}>
            <div className="metric-row" style={{border:0,paddingTop:0,marginTop:2}}><span className="k">内存占用</span><span className="v">{s.mem.toFixed(1)} / 32 GB</span></div>
            <div className="bar" style={{margin:'8px 0 18px'}}><i style={{width: (s.mem/32*100) + '%'}}></i></div>
            <div className="metric-row" style={{border:0,paddingTop:0,marginTop:0}}><span className="k">硬盘占用</span><span className="v">{s.disk.toFixed(1)} / 2.0 TB</span></div>
            <div className="bar" style={{marginTop:'8px'}}><i style={{width:'50%',background:'linear-gradient(90deg,var(--accent),var(--primary))'}}></i></div>
          </div>
        </div>
      </div>

      {/* 风扇信息 */}
      <div className="card fan-card reveal enter" style={{animationDelay:'.26s'}}>
        <div className="head">
          <span className="t"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg></span>风扇信息</span>
          <span style={{fontSize:'11.5px',color:'var(--fg-3)'}}>EC 寄存器读取 · 只读</span>
        </div>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>大风扇</span>
          <div className="bar"><i style={{width: Math.round((s.fan1 - 1000) / 50) + '%'}}></i></div>
          <span className="rpm"><b>{Math.round(s.fan1)}</b> RPM<small>EC 直读</small></span>
        </div>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>小风扇</span>
          <div className="bar"><i style={{width: Math.round((s.fan2 - 1000) / 60) + '%'}}></i></div>
          <span className="rpm"><b>{Math.round(s.fan2)}</b> RPM<small>EC 直读</small></span>
        </div>
      </div>
    </section>
  );
}
