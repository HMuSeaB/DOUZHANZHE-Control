import { useState, useEffect } from "react";
import { useControlState } from "../hooks/useControlState";
import FanCurvePanel from "../components/panels/FanCurvePanel";

export default function FanControl() {
  const { telemetry } = useControlState();
  const [curveActive, setCurveActive] = useState(false);
  const [fan1Pct, setFan1Pct] = useState(80);
  const [fan2Pct, setFan2Pct] = useState(60);

  const fan1Rpm = telemetry?.fan?.rpm?.[0] ?? 0;
  const fan2Rpm = telemetry?.fan?.rpm?.[1] ?? 0;
  const fan1TelePct = telemetry?.fan?.pct?.[0] ?? 0;
  const fan2TelePct = telemetry?.fan?.pct?.[1] ?? 0;

  useEffect(() => {
    import("../services/uxtuAdapter").then(({ fetchFanCurveStatus }) =>
      fetchFanCurveStatus().then((s) => { if (s?.ok) setCurveActive(s.active); })
    ).catch(() => {});
  }, []);

  const setFanTarget = async (fanIdx, pct) => {
    try {
      await fetch("/api/fan/set-target", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fan: fanIdx, target: pct }),
      });
    } catch {}
  };

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>风扇控制</h1>
          <p>EC 寄存器绑定 · 手动调速与自定义曲线互斥 · 仅斗战者机型可见</p>
        </div>
      </div>

      {/* 实时监控 */}
      <div className="section-title">实时监控<span className="line"></span></div>
      <div className="card reveal enter" style={{ padding: "6px 20px", animationDelay: ".02s" }}>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>大风扇</span>
          <div className="bar"><i style={{ width: fan1TelePct + "%" }}></i></div>
          <span className="rpm"><b>{fan1Rpm}</b> RPM<small>目标 {fan1TelePct}%</small></span>
        </div>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>小风扇</span>
          <div className="bar"><i style={{ width: fan2TelePct + "%" }}></i></div>
          <span className="rpm"><b>{fan2Rpm}</b> RPM<small>目标 {fan2TelePct}%</small></span>
        </div>
      </div>

      {/* 手动调速 */}
      <div className="section-title">手动调速<span className={"tag" + (curveActive ? "" : " hidden")} id="manualTag" style={curveActive ? {} : {display:"none"}}>曲线运行时禁用</span><span className="line"></span></div>
      <div className="card reveal enter" style={{ padding: "6px 20px 12px", animationDelay: ".06s" }}>
        <div className="param">
          <span className="pk"><b>大风扇目标</b><small>固定转速百分比</small></span>
          <input type="range" className="slider fan-manual" min="0" max="100" value={fan1Pct} disabled={curveActive}
            onChange={e => { const v = Number(e.target.value); setFan1Pct(v); setFanTarget(0, v); }} />
          <span className="pv">{fan1Pct} <small>%</small></span>
        </div>
        <div className="param">
          <span className="pk"><b>小风扇目标</b><small>固定转速百分比</small></span>
          <input type="range" className="slider fan-manual" min="0" max="100" value={fan2Pct} disabled={curveActive}
            onChange={e => { const v = Number(e.target.value); setFan2Pct(v); setFanTarget(1, v); }} />
          <span className="pv">{fan2Pct} <small>%</small></span>
        </div>
        <div className="hint">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M12 9v4M12 17h.01"/><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg>
          手动调速与自定义曲线互斥：启用曲线后手动滑块自动灰化，停止曲线后恢复。
        </div>
      </div>

      {/* 自定义风扇曲线 */}
      <div className="section-title">自定义风扇曲线<span className="line"></span></div>
      <div className="card reveal enter" style={{ animationDelay: ".1s" }}>
        <FanCurvePanel telemetry={telemetry} onCurveActiveChange={setCurveActive} />
      </div>
    </section>
  );
}