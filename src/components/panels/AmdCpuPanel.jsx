import { useCallback, useRef, useEffect } from "react";
import { applyHardwareControl, powerPlanHALMap, setCpuFreqLimit, setCpuTurbo, setCpuCoreLimitPercent } from "../../services/uxtuAdapter";

const POWER_PLANS = [
  { id: "efficiency", label: "最高能效", halValue: powerPlanHALMap.efficiency },
  { id: "balance", label: "平衡", halValue: powerPlanHALMap.balance },
  { id: "performance", label: "最佳性能", halValue: powerPlanHALMap.performance },
];

export default function AmdCpuPanel({ settings, uxtuParams, setUxtuParams, overrides, saveOverride, clearOverride, switching }) {
  const latestModeRef = useRef(settings.mode);
  latestModeRef.current = settings.mode;
  const paramsLocked = !!switching;
  const isSet = (key) => Object.prototype.hasOwnProperty.call(overrides, key);
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
    <>
      {(() => {
        const freqSet = !!uxtuParams.cpuFreqLimitEnabled;
        return (
          <>
            <div className="switch-row">
              <span className="k">频率限制</span>
              <button className={"switch-track" + (freqSet ? " on" : " off")} disabled={paramsLocked} onClick={async () => {
                if (freqSet) {
                  await clearOverride(settings.mode, ['cpuFreqLimitEnabled', 'cpuFreqLimitMhz']);
                } else {
                  update("cpuFreqLimitEnabled")(true);
                  update("cpuFreqLimitMhz")(uxtuParams.cpuFreqLimitMhz);
                  queueCpuFreq(uxtuParams.cpuFreqLimitMhz);
                }
              }}><span className="thumb"></span></button>
            </div>
            {freqSet && (
              <div className="slider-group">
                <div className="slider-label"><span className="k">最大频率</span><span className="v">{uxtuParams.cpuFreqLimitMhz}<span className="u">MHz</span></span></div>
                <input type="range" className="slider-track" min={2000} max={5500} step={100} value={uxtuParams.cpuFreqLimitMhz} onChange={e => { const v = Number(e.target.value); update("cpuFreqLimitMhz")(v); queueCpuFreq(v); }} />
              </div>
            )}
          </>
        );
      })()}
      {(() => {
        const coreSet = isSet('cpuCoreLimit') && uxtuParams.cpuCoreLimit > 0;
        return (
          <>
            <div className="switch-row">
              <span className="k">限制核心数</span>
              <button className={"switch-track" + (coreSet ? " on" : " off")} disabled={paramsLocked} onClick={async () => {
                if (coreSet) {
                  await clearOverride(settings.mode, ['cpuCoreLimit']);
                } else {
                  const v = 8;
                  update("cpuCoreLimit")(v);
                  queueCoreLimit(v);
                }
              }}><span className="thumb"></span></button>
            </div>
            {coreSet && (
              <div className="slider-group">
                <div className="slider-label"><span className="k">核心数</span><span className="v">{uxtuParams.cpuCoreLimit}<span className="u">核</span></span></div>
                <input type="range" className="slider-track" min={2} max={18} step={2} value={uxtuParams.cpuCoreLimit} onChange={e => { const v = Number(e.target.value); update("cpuCoreLimit")(v); queueCoreLimit(v); }} disabled={paramsLocked} />
              </div>
            )}
          </>
        );
      })()}
      <div className="switch-row">
        <span className="k">睿频加速</span>
        <button className={"switch-track" + (!uxtuParams.cpuTurboDisabled ? " on" : " off")} disabled={paramsLocked} onClick={() => { const v = !uxtuParams.cpuTurboDisabled; update("cpuTurboDisabled")(v); queueTurbo(v); }}><span className="thumb"></span></button>
      </div>
      {(() => {
        const planSet = isSet('cpuPowerPlan');
        return (
          <>
            <div className="switch-row" style={{ marginTop: 8 }}>
              <span className="k">电源管理</span>
              <button className={"switch-track" + (planSet ? " on" : " off")} disabled={paramsLocked} onClick={async () => {
                if (planSet) {
                  await clearOverride(settings.mode, ['cpuPowerPlan']);
                } else {
                  update("cpuPowerPlan")(uxtuParams.cpuPowerPlan);
                  const halValue = powerPlanHALMap[uxtuParams.cpuPowerPlan];
                  if (halValue !== undefined) applyHardwareControl("power_plan", halValue, settings.mode).catch(() => {});
                }
              }}><span className="thumb"></span></button>
            </div>
            {planSet && (
              <div style={{ display: "flex", gap: 6 }}>
                {POWER_PLANS.map(plan => (
                  <button key={plan.id} onClick={() => { update("cpuPowerPlan")(plan.id); if (plan.halValue !== undefined) applyHardwareControl("power_plan", plan.halValue, settings.mode).catch(() => {}); }} disabled={paramsLocked}
                    style={{ flex: 1, padding: "8px 12px", borderRadius: 8, border: "1px solid var(--stroke)", background: uxtuParams.cpuPowerPlan === plan.id ? "var(--primary)" : "color-mix(in srgb, var(--surface-2) 70%, transparent)", color: uxtuParams.cpuPowerPlan === plan.id ? "var(--primary-fg)" : "var(--fg-2)", cursor: "pointer", fontSize: 12, fontFamily: "inherit" }}
                  >{plan.label}</button>
                ))}
              </div>
            )}
          </>
        );
      })()}
    </>
  );
}
