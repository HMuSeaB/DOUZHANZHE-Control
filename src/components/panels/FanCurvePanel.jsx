import { useState, useEffect, useRef, useCallback } from "react";
import { fetchFanCurveStatus, saveFanCurve, startFanCurve, stopFanCurve, fetchRouteInfo, FULL_FAN_RANGE, reapplyOverrides } from "../../services/uxtuAdapter";
const VB_W = 560, VB_H = 300, PL = 50, PR = 20, PT = 30, PB = 36;
const CW = VB_W - PL - PR, CH = VB_H - PT - PB;
const T_MIN = 40, T_MAX = 100;
const DEFAULT_POINTS = [
  { temp: 40, largeRpm: 1932, smallRpm: 1700 },
  { temp: 45, largeRpm: 2058, smallRpm: 1850 },
  { temp: 50, largeRpm: 2184, smallRpm: 2016 },
  { temp: 55, largeRpm: 2604, smallRpm: 3528 },
  { temp: 60, largeRpm: 2940, smallRpm: 4788 },
  { temp: 65, largeRpm: 3192, smallRpm: 5880 },
  { temp: 70, largeRpm: 3528, smallRpm: 6384 },
  { temp: 75, largeRpm: 3780, smallRpm: 6888 },
  { temp: 80, largeRpm: 4032, smallRpm: 7476 },
  { temp: 85, largeRpm: 4284, smallRpm: 7980 },
  { temp: 90, largeRpm: 4368, smallRpm: 8200 },
  { temp: 95, largeRpm: 4368, smallRpm: 8200 },
  { temp: 100, largeRpm: 4368, smallRpm: 8200 },
];
const tX = (t) => PL + ((t - T_MIN) / (T_MAX - T_MIN)) * CW;
function pY(r) { return PT + CH - (r / 8400) * CH; }
function yR(y) { return ((PT + CH - y) / CH) * 8400; }
const xT = (x) => T_MIN + ((x - PL) / CW) * (T_MAX - T_MIN);
const snap5 = (v) => Math.round(v / 5) * 5;
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
function buildPath(pts, key) {
  const sorted = [...pts].sort((a, b) => a.temp - b.temp);
  return sorted.map((p, i) => `${i === 0 ? "M" : "L"}${tX(p.temp).toFixed(1)},${pY(p[key]).toFixed(1)}`).join(" ");
}
const Y_TICKS = [0, 2100, 4200, 6300, 8400];
const X_TICKS = [40, 55, 70, 85, 100];
const X_TP = X_TICKS.map(tX);
export default function FanCurvePanel({ telemetry, onCurveActiveChange }) {
  const [points, setPoints] = useState(DEFAULT_POINTS);
  const [curveActive, setCurveActive] = useState(false);
  const [activeFan, setActiveFan] = useState("big");
  const [routeInfo, setRouteInfo] = useState(null);
  const [readout, setReadout] = useState("拖动控制点调参");
  const svgRef = useRef(null);
  const dragRef = useRef(null);
  useEffect(() => {
    fetchFanCurveStatus().then((s) => {
      if (s.ok) {
        setCurveActive(s.active);
        if (onCurveActiveChange) onCurveActiveChange(s.active);
        if (s.points?.length >= 2) {
          let loaded = s.points.map((p) => ({ temp: p.temp, largeRpm: p.largeRpm, smallRpm: p.smallRpm }));
          if (!loaded.find((p) => p.temp === 45)) {
            const p40 = loaded.find((p) => p.temp === 40);
            const p50 = loaded.find((p) => p.temp === 50);
            if (p40 && p50) { loaded.push({ temp: 45, largeRpm: Math.round((p40.largeRpm + p50.largeRpm) / 2), smallRpm: Math.round((p40.smallRpm + p50.smallRpm) / 2) }); loaded.sort((a, b) => a.temp - b.temp); }
          }
          setPoints(loaded);
        }
      }
    }).catch(() => {});
    fetchRouteInfo().then((r) => { if (r?.ok) setRouteInfo(r); }).catch(() => {});
  }, []);
  const sorted = [...points].map((p, i) => ({ ...p, _i: i })).sort((a, b) => a.temp - b.temp);
  const handleMouseDown = useCallback((e, idx) => {
    const realIdx = sorted[idx]._i;
    dragRef.current = { idx: realIdx, startX: e.clientX, startY: e.clientY, origPoint: { ...points[realIdx] } };
  }, [sorted, points]);
  const handleMouseMove = useCallback((e) => {
    const drag = dragRef.current;
    if (!drag) return;
    const svg = svgRef.current;
    if (!svg) return;
    const rect = svg.getBoundingClientRect();
    const scaleX = VB_W / rect.width;
    const scaleY = VB_H / rect.height;
    const mx = (e.clientX - rect.left) * scaleX;
    const my = (e.clientY - rect.top) * scaleY;
    const newTemp = snap5(clamp(xT(mx), T_MIN, T_MAX));
    const [rMin, rMax] = activeFan === "big" ? [1900, 4400] : [1700, 8200];
    const newRpm = Math.round(clamp(yR(my), rMin, rMax) / 100) * 100;
    setPoints((prev) => {
      const next = [...prev];
      const idx = drag.idx;
      const old = next[idx] || drag.origPoint;
      const key = activeFan === "big" ? "largeRpm" : "smallRpm";
      next[idx] = { ...old, temp: newTemp, [key]: newRpm };
      setReadout(`${newTemp}°C / ${newRpm} RPM`);
      return next;
    });
  }, [activeFan]);
  const handleMouseUp = useCallback(() => { dragRef.current = null; }, []);
  const handleApply = useCallback(async () => {
    const res = await saveFanCurve(points);
    if (res?.ok) {
      const started = await startFanCurve();
      if (started?.ok) { setCurveActive(true); if (onCurveActiveChange) onCurveActiveChange(true); }
    }
  }, [points, onCurveActiveChange]);
  const handleStop = useCallback(async () => {
    const res = await stopFanCurve();
    if (res?.ok) { setCurveActive(false); if (onCurveActiveChange) onCurveActiveChange(false); try { await reapplyOverrides(); } catch {} }
  }, [onCurveActiveChange]);
  const handleVsInput = useCallback((idx, key, val) => {
    const [rMin, rMax] = key === "largeRpm" ? [1900, 4400] : [1700, 8200];
    const rpm = Math.round(clamp(parseInt(val) || 0, rMin, rMax) / 100) * 100;
    setPoints((prev) => { const next = [...prev]; next[idx] = { ...next[idx], [key]: rpm }; return next; });
  }, []);
  const bigPath = buildPath(sorted, "largeRpm");
  const smallPath = buildPath(sorted, "smallRpm");
  return (<div className="curve-wrap" onMouseMove={handleMouseMove} onMouseUp={handleMouseUp} onMouseLeave={handleMouseUp}>
      <svg className="curve-svg" ref={svgRef} viewBox={"0 0 " + VB_W + " " + VB_H} role="img" aria-label="风扇温度-转速曲线">
        <g className="grid-l">{Y_TICKS.map((t, i) => <line key={"g" + i} x1={PL} y1={pY(t)} x2={PL + CW} y2={pY(t)} />)}</g>
        {Y_TICKS.map((t, i) => <text key={"y" + i} className="axis-t" x={PL - 8} y={pY(t) + 4} textAnchor="end">{t}</text>)}
        {X_TICKS.map((t, i) => <text key={"x" + i} className="axis-t" x={X_TP[i]} y={VB_H - 6} textAnchor="middle">{t}°C</text>)}
        <text className="axis-t" x={PL + CW / 2} y={VB_H - 2} textAnchor="middle" style={{fontSize:11,fill:"var(--fg-3)"}}>温度</text>
        <polyline points={bigPath} fill="none" stroke="var(--primary)" strokeWidth="2.5" strokeLinejoin="round" />
        <polyline points={smallPath} fill="none" stroke="var(--accent)" strokeWidth="2.5" strokeLinejoin="round" strokeDasharray="5 4" />
        {sorted.map((p, i) => <circle key={"b" + p._i} className="dot" cx={tX(p.temp)} cy={pY(p.largeRpm)} r={activeFan === "big" ? 5 : 3.5} fill="var(--primary)" stroke="var(--surface)" strokeWidth="2" style={{opacity: activeFan === "big" ? 1 : 0.4, cursor:"pointer"}} onMouseDown={(e) => activeFan === "big" && handleMouseDown(e, i)} />)}
        {sorted.map((p, i) => <circle key={"s" + p._i} className="dot" cx={tX(p.temp)} cy={pY(p.smallRpm)} r={activeFan === "small" ? 5 : 3.5} fill="var(--accent)" stroke="var(--surface)" strokeWidth="2" style={{opacity: activeFan === "small" ? 1 : 0.4, cursor:"pointer"}} onMouseDown={(e) => activeFan === "small" && handleMouseDown(e, i)} />)}
      </svg>
      <div className="curve-side">
        <div className="legend">
          <div className={"li" + (activeFan === "big" ? " active" : "")} onClick={() => setActiveFan("big")}><span className="swatch" style={{background:"var(--primary)"}}></span>大风扇<small>{points.length} 控制点</small></div>
          <div className={"li" + (activeFan === "small" ? " active" : "")} onClick={() => setActiveFan("small")}><span className="swatch" style={{background:"var(--accent)"}}></span>小风扇<small>{points.length} 控制点</small></div>
        </div>
        <div className="curve-readout">{readout}</div>
        <div className="curve-status">状态：<b>{curveActive ? "运行中" : "未运行"}</b><br />ITSM 模式：<b>{routeInfo?.mode || "—"}</b><br />偏离计数：<b>{routeInfo?.deviation ?? 0}</b></div>
        <div className="curve-actions">
          <button className="btn primary" onClick={handleApply} disabled={curveActive}><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9"><path d="M20 6 9 17l-5-5"/></svg>应用曲线</button>
          <button className="btn" onClick={handleStop} disabled={!curveActive}>停止</button>
        </div>
      </div>
      <div className="value-strip">{sorted.map((p) => {const key = activeFan === "big" ? "largeRpm" : "smallRpm"; return (<div key={p._i} className="vs-col"><div className="vs-temp">{p.temp}°C</div><input className="vs-input" type="text" inputMode="numeric" value={p[key] ?? 0} onChange={(e) => handleVsInput(p._i, key, e.target.value)} /></div>);})}</div>
    </div>);
}
