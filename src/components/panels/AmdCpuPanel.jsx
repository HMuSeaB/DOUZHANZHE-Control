import { useCallback, useRef, useEffect } from "react";
import { applyHardwareControl, powerPlanHALMap, setCpuFreqLimit, setCpuTurbo, setCpuCoreLimitPercent } from "../../services/uxtuAdapter";

const POWER_PLANS = [
  { id: "efficiency", label: "最高能效", halValue: powerPlanHALMap.efficiency },
  { id: "balance", label: "平衡", halValue: powerPlanHALMap.balance },
  { id: "performance", label: "最佳性能", halValue: powerPlanHALMap.performance },
];

export default function AmdCpuPanel({ settings, uxtuParams, setUxtuParams, overrides, saveOverride, switching, customLabel }) {
  const latestModeRef = useRef(settings.mode);
  latestModeRef.current = settings.mode;
  const paramsLocked = !!switching;
  const update = useCallback((key) => (value) => {
    setUxtuParams(p => ({ ...p, [key]: value }));
    saveOverride?.(settings.mode, key, value);
  }, [setUxtuParams, saveOverride, settings.mode]);
  const cpuFreqTimer = useRef(null);
  const coreTimer = useRef(null);
  const turboTimer = useRef(null);
  useEffect(() => () => { clearTimeout(cpuFreqTimer.current); clearTimeout(coreTimer.current); clearTimeout(turboTimer.current); }, []);
  function queueCpuFreq(mhz) { clearTimeout(cpuFreqTimer.current); cpuFreqTimer.current = setTimeout(async () => { try { await setCpuFreqLimit(mhz, latestModeRef.current); } catch (err) { console.error('CPU freq-limit failed:', err); } }, 600); }
    function queueTurbo(disabled) {
    clearTimeout(turboTimer.current);
    turboTimer.current = setTimeout(async () => {
      try { await setCpuTurbo(!disabled, latestModeRef.current); } catch (err) { update("cpuTurboDisabled")(!disabled); console.error("Turbo failed:", err); }
    }, 600);
  }

  function queueCoreLimit(coreCount) { clearTimeout(coreTimer.current); coreTimer.current = setTimeout(async () => { try { const percent = coreCount > 0 ? Math.round(coreCount / 16 * 100) : 100; await setCpuCoreLimitPercent(percent, latestModeRef.current); } catch (err) { console.error('Core limit failed:', err); } }, 600); }
  return (
    <div className="card" style={{ padding: 20 }}>
      <div className="head" style={{ marginBottom: 18 }}>
        <span className="t"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="4" y="4" width="16" height="16" rx="2"/><path d="M8 2v4M16 2v4M2 8h4M2 16h4M18 8h4M18 16h4M8 20v2M16 20v2"/></svg></span>CPU 频率控制{customLabel}</span>
      </div>
      <div className="switch-row">
        <span className="k">频率限制</span>
        <button className={"switch-track" + (uxtuParams.cpuFreqLimitEnabled ? " on" : " off")} disabled={paramsLocked} onClick={() => { const on = !uxtuParams.cpuFreqLimitEnabled; update("cpuFreqLimitEnabled")(on); queueCpuFreq(on ? uxtuParams.cpuFreqLimitMhz : 0); }}><span className="thumb"></span></button>
      </div>
      {uxtuParams.cpuFreqLimitEnabled && (
        <div className="slider-group">
          <div className="slider-label"><span className="k">最大频率</span><span className="v">{uxtuParams.cpuFreqLimitMhz}<span className="u">MHz</span></span></div>
          <input type="range" className="slider-track" min={2000} max={5500} step={100} value={uxtuParams.cpuFreqLimitMhz} onChange={e => { const v = Number(e.target.value); update("cpuFreqLimitMhz")(v); queueCpuFreq(v); }} />
        </div>
      )}
      <div className="switch-row">
        <span className="k">限制核心数</span>
        <button className={"switch-track" + (uxtuParams.cpuCoreLimit > 0 ? " on" : " off")} disabled={paramsLocked} onClick={() => { const on = uxtuParams.cpuCoreLimit <= 0; const v = on ? 8 : 0; update("cpuCoreLimit")(v); queueCoreLimit(v); }}><span className="thumb"></span></button>
      </div>
      {uxtuParams.cpuCoreLimit > 0 && (
        <div className="slider-group">
          <div className="slider-label"><span className="k">核心数</span><span className="v">{uxtuParams.cpuCoreLimit}<span className="u">核</span></span></div>
          <input type="range" className="slider-track" min={2} max={18} step={2} value={uxtuParams.cpuCoreLimit} onChange={e => { const v = Number(e.target.value); update("cpuCoreLimit")(v); queueCoreLimit(v); }} disabled={paramsLocked} />
        </div>
      )}
      <div className="switch-row">
        <span className="k">睿频加速</span>
        <button className={"switch-track" + (!uxtuParams.cpuTurboDisabled ? " on" : " off")} disabled={paramsLocked} onClick={() => { const v = !uxtuParams.cpuTurboDisabled; update("cpuTurboDisabled")(v); queueTurbo(v); }}><span className="thumb"></span></button>
      </div>
      <div style={{ marginTop: 16 }}>
        <p style={{ marginBottom: 8, color: "var(--fg-3)", fontSize: 12 }}>电源管理</p>
        <div style={{ display: "flex", gap: 6 }}>
          {POWER_PLANS.map(plan => (
            <button key={plan.id} onClick={() => { update("cpuPowerPlan")(plan.id); if (plan.halValue !== undefined) applyHardwareControl("power_plan", plan.halValue, settings.mode).catch(() => {}); }} disabled={paramsLocked}
              style={{ flex: 1, padding: "8px 12px", borderRadius: 8, border: "1px solid var(--stroke)", background: uxtuParams.cpuPowerPlan === plan.id ? "var(--primary)" : "color-mix(in srgb, var(--surface-2) 70%, transparent)", color: uxtuParams.cpuPowerPlan === plan.id ? "var(--primary-fg)" : "var(--fg-2)", cursor: "pointer", fontSize: 12, fontFamily: "inherit" }}
            >{plan.label}</button>
          ))}
        </div>
      </div>
    </div>
  );
}