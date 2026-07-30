import { useCallback, useRef, useEffect } from "react";
import { applySmuSet } from "../../services/uxtuAdapter";

export default function AmdPowerPanel({ settings, uxtuParams, setUxtuParams, overrides, saveOverride, switching, customLabel }) {
  const latestModeRef = useRef(settings.mode);
  latestModeRef.current = settings.mode;
  const paramsLocked = !!switching;
  const update = useCallback((key) => (value) => {
    setUxtuParams(p => ({ ...p, [key]: value }));
    saveOverride?.(settings.mode, key, value);
  }, [setUxtuParams, saveOverride, settings.mode]);
  const smuTimer = useRef(null);
  useEffect(() => () => clearTimeout(smuTimer.current), []);
  function queueSmu(param, val) {
    clearTimeout(smuTimer.current);
    smuTimer.current = setTimeout(async () => {
      try { await applySmuSet(param, val, latestModeRef.current); }
      catch (err) { console.error("SMU set failed:", err); }
    }, 600);
  }
  return (
    <div className="card" style={{ padding: 20 }}>
      <div className="head" style={{ marginBottom: 18 }}>
        <span className="t"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M13 2 3 14h7l-1 8 10-12h-7l1-8Z"/></svg></span>CPU 超频与功耗{customLabel}</span>
      </div>
      <div className="slider-group">
        <div className="slider-label"><span className="k">长时功耗</span><span className="v">{uxtuParams.cpuLongPptW}<span className="u">W</span></span></div>
        <input type="range" className="slider-track" min={15} max={120} step={1} value={uxtuParams.cpuLongPptW} onChange={e => { const v = Number(e.target.value); update("cpuLongPptW")(v); queueSmu("stapm_limit", v); }} disabled={paramsLocked} />
      </div>
      <div className="slider-group">
        <div className="slider-label"><span className="k">短时功耗</span><span className="v">{uxtuParams.cpuShortPptW}<span className="u">W</span></span></div>
        <input type="range" className="slider-track" min={15} max={140} step={1} value={uxtuParams.cpuShortPptW} onChange={e => { const v = Number(e.target.value); update("cpuShortPptW")(v); queueSmu("short_power_limit", v); }} disabled={paramsLocked} />
      </div>
      <div className="slider-group">
        <div className="slider-label"><span className="k">温度墙</span><span className="v">{uxtuParams.cpuTempLimitC}<span className="u">°C</span></span></div>
        <input type="range" className="slider-track" min={60} max={100} step={1} value={uxtuParams.cpuTempLimitC} onChange={e => { const v = Number(e.target.value); update("cpuTempLimitC")(v); queueSmu("tctl_temp", v); }} disabled={paramsLocked} />
      </div>
      <div className="slider-group">
        <div className="slider-label"><span className="k">电压调节 · 降压</span><span className="v">{uxtuParams.cpuVoltageOffset}<span className="u">mV</span></span></div>
        <input type="range" className="slider-track" min={-30} max={0} step={1} value={uxtuParams.cpuVoltageOffset} onChange={e => { const v = Number(e.target.value); update("cpuVoltageOffset")(v); queueSmu("co_all", v); }} disabled={paramsLocked} />
      </div>
    </div>
  );
}