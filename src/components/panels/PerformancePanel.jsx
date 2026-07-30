import { useState, useEffect, useCallback, useRef } from "react";
import { applySmuSet, applyHardwareControl, powerPlanHALMap, applyGpuControl, applyNvapiOverclock, applyNvapiThermalLimit, setCpuFreqLimit, setCpuTurbo, setCpuCoreLimitPercent } from "../../services/uxtuAdapter";
import { useToast } from "../ui/Toast";

const POWER_PLANS = [
  { id: "efficiency", label: "最高能效", halValue: powerPlanHALMap.efficiency },
  { id: "balance", label: "平衡", halValue: powerPlanHALMap.balance },
  { id: "performance", label: "最佳性能", halValue: powerPlanHALMap.performance },
];

function Slider({ label, value, min, max, step, unit, disabled, onChange, displayValue, action }) {
  return (
    <div className="slider-group">
      <div className="slider-label">
        <span className="k">{label}</span>
        <span className="v">{displayValue ?? value}<span className="u">{displayValue ? "" : unit}</span>{action}</span>
      </div>
      <input type="range" className="slider-track" min={min} max={max} step={step} value={value} disabled={disabled} onChange={e => onChange(Number(e.target.value))} />
    </div>
  );
}

function Switch({ label, checked, disabled, onChange }) {
  return (
    <div className="switch-row">
      <span className="k">{label}</span>
      <button className={"switch-track" + (checked ? " on" : " off")} disabled={disabled} onClick={() => onChange(!checked)}><span className="thumb"></span></button>
    </div>
  );
}
export default function PerformancePanel({
  settings, setSettings, uxtuParams, setUxtuParams,
  showCpu = true, showGpu = true, showPower = true, editMode = false,
  overrides, saveOverride, customLabel, gpuMode, switching,
}) {
  const toast = useToast();
  const gpuClockDisabled = gpuMode === 0 || gpuMode === 2;
  const gpuAllDisabled = gpuMode === 2;
  const paramsLocked = !!switching;
  const latestParamsRef = useRef(uxtuParams);
  latestParamsRef.current = uxtuParams;
  const latestModeRef = useRef(settings.mode);
  latestModeRef.current = settings.mode;
  const smuTimer = useRef(null);
  const cpuFreqTimer = useRef(null);
  const coreTimer = useRef(null);
  const ocTimer = useRef(null);
  const turboTimer = useRef(null);
  const gpuMemTimer = useRef(null);
  const thermalTimer = useRef(null);
  const gpuCoreTimer = useRef(null);
  useEffect(() => () => {
    clearTimeout(smuTimer.current); clearTimeout(cpuFreqTimer.current);
    clearTimeout(coreTimer.current); clearTimeout(ocTimer.current);
    clearTimeout(turboTimer.current); clearTimeout(gpuMemTimer.current);
    clearTimeout(thermalTimer.current); clearTimeout(gpuCoreTimer.current);
  }, []);
  const update = useCallback((key) => (value) => {
    setUxtuParams(p => ({ ...p, [key]: value }));
    saveOverride?.(settings.mode, key, value);
  }, [setUxtuParams, saveOverride, settings.mode]);
  async function gpuCmd(action, value, retries = 2) {
    for (let i = 0; i <= retries; i++) {
      try { const r = await applyGpuControl(action, value, undefined, undefined, latestModeRef.current); if (r?.ok) return r; } catch (err) { if (i === retries) throw err; }
      await new Promise(r => setTimeout(r, 300));
    }
  }
  async function applyGpuCoreFreq(mhz) {
    if (latestParamsRef.current.gpuFreqLimitEnabled) {
      await gpuCmd('reset-clocks').catch(() => {});
      await gpuCmd('limit-max', mhz);
      await gpuCmd('lock-exact', mhz);
    } else {
      await gpuCmd('reset-clocks').catch(() => {});
      await gpuCmd('limit-max', mhz);
    }
  }
  function queueGpuCore(mhz) { clearTimeout(gpuCoreTimer.current); gpuCoreTimer.current = setTimeout(() => applyGpuCoreFreq(mhz), 400); }
  async function toggleGpuLock() {
    const mhz = latestParamsRef.current.gpuCoreFreqMhz;
    if (latestParamsRef.current.gpuFreqLimitEnabled) {
      await gpuCmd('reset-clocks').catch(() => {});
      await gpuCmd('limit-max', mhz);
      update('gpuFreqLimitEnabled')(false);
    } else {
      await gpuCmd('limit-max', mhz);
      await gpuCmd('lock-exact', mhz);
      update('gpuFreqLimitEnabled')(true);
      update('gpuCoreFreqMhz')(mhz);
    }
  }
  function queueCpuFreq(mhz) { clearTimeout(cpuFreqTimer.current); cpuFreqTimer.current = setTimeout(async () => { try { await setCpuFreqLimit(mhz, latestModeRef.current); } catch (err) { console.error('CPU freq limit failed:', err); } }, 600); }
  function queueSmu(parameter, valueM) { clearTimeout(smuTimer.current); smuTimer.current = setTimeout(async () => { try { await applySmuSet(parameter, valueM, latestModeRef.current); } catch (err) { console.error('SMU set failed:', err); } }, 600); }
  function queueTurbo(disabled) { clearTimeout(turboTimer.current); turboTimer.current = setTimeout(async () => { try { await setCpuTurbo(!disabled, latestModeRef.current); } catch (err) { update('cpuTurboDisabled')(!disabled); console.error('Turbo switch failed:', err); } }, 600); }
  function queueOc() { clearTimeout(ocTimer.current); ocTimer.current = setTimeout(async () => { try { const p = latestParamsRef.current; await applyNvapiOverclock(p.ocCoreOffsetMhz ?? 0, p.ocVoltOffsetMv ?? 0, latestModeRef.current); } catch (err) { console.error('OC failed:', err); } }, 600); }
  function queueGpuMem(level) { clearTimeout(gpuMemTimer.current); gpuMemTimer.current = setTimeout(async () => { try { await applyNvapiOverclock(undefined, undefined, latestModeRef.current, level); } catch (err) { console.error('GPU mem failed:', err); } }, 600); }
  function queueThermal(limit) { clearTimeout(thermalTimer.current); thermalTimer.current = setTimeout(async () => { try { await applyNvapiThermalLimit(limit, latestModeRef.current); } catch (err) { console.error('Thermal limit failed:', err); } }, 600); }
  function queueCoreLimit(coreCount) { clearTimeout(coreTimer.current); coreTimer.current = setTimeout(async () => { try { const percent = coreCount > 0 ? Math.round(coreCount / 16 * 100) : 100; await setCpuCoreLimitPercent(percent, latestModeRef.current); } catch (err) { console.error('Core limit failed:', err); } }, 600); }  return (<>{showCpu && <div className="card" style={{ padding: 20 }}>
      <div className="head" style={{ marginBottom: 18 }}>
        <span className="t"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="6" y="6" width="12" height="12" rx="1.5"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/></svg></span>CPU 调节{customLabel}</span>
      </div>
      <Switch label="频率限制" checked={uxtuParams.cpuFreqLimitEnabled} disabled={paramsLocked} onChange={on => { update("cpuFreqLimitEnabled")(on); queueCpuFreq(on ? uxtuParams.cpuFreqLimitMhz : 0); }} />
      {uxtuParams.cpuFreqLimitEnabled && <Slider label="最大频率" value={uxtuParams.cpuFreqLimitMhz} min={2000} max={5500} step={100} unit="MHz" disabled={paramsLocked} onChange={v => { update("cpuFreqLimitMhz")(v); queueCpuFreq(v); }} />}
      <Switch label="限制核心数" checked={uxtuParams.cpuCoreLimit > 0} disabled={paramsLocked} onChange={on => { const v = on ? 8 : 0; update("cpuCoreLimit")(v); queueCoreLimit(v); }} />
      {uxtuParams.cpuCoreLimit > 0 && <Slider label="核心数" value={uxtuParams.cpuCoreLimit} min={2} max={18} step={2} unit="核" disabled={paramsLocked} onChange={v => { update("cpuCoreLimit")(v); queueCoreLimit(v); }} />}
      <div style={{ marginTop: 16 }}>
        <p style={{ marginBottom: 8, color: "var(--fg-3)", fontSize: 12 }}>电源管理</p>
        <div style={{ display: "flex", gap: 6 }}>
          {POWER_PLANS.map(plan => <button key={plan.id} onClick={() => { update("cpuPowerPlan")(plan.id); if (plan.halValue !== undefined) applyHardwareControl("power_plan", plan.halValue, settings.mode).catch(() => {}); }} disabled={paramsLocked} style={{ flex: 1, padding: "8px 12px", borderRadius: 8, border: "1px solid var(--stroke)", background: uxtuParams.cpuPowerPlan === plan.id ? "var(--primary)" : "color-mix(in srgb, var(--surface-2) 70%, transparent)", color: uxtuParams.cpuPowerPlan === plan.id ? "var(--primary-fg)" : "var(--fg-2)", cursor: "pointer", fontSize: 12, fontFamily: "inherit" }}>{plan.label}</button>)}
        </div>
      </div>
      <Switch label="睿频加速" checked={!uxtuParams.cpuTurboDisabled} disabled={paramsLocked} onChange={on => { const v = !on; update("cpuTurboDisabled")(v); queueTurbo(v); }} />
    </div>}

    {showPower && <div className="card" style={{ padding: 20 }}>
      <div className="head" style={{ marginBottom: 18 }}>
        <span className="t"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M13 2 3 14h7l-1 8 10-12h-7l1-8Z"/></svg></span>CPU 功耗与温度{customLabel}</span>
      </div>
      <Slider label="温度墙" value={uxtuParams.cpuTempLimitC} min={60} max={100} unit="°C" disabled={paramsLocked} onChange={v => { update("cpuTempLimitC")(v); queueSmu("temp_limit", v); }} />
      <Slider label="电压调节(降压)" value={uxtuParams.cpuVoltageOffset} min={-30} max={0} step={1} unit="mV" disabled={paramsLocked} onChange={v => { update("cpuVoltageOffset")(v); queueSmu("co_all", v); }} />
      <Slider label="长时功耗" value={uxtuParams.cpuLongPptW} min={15} max={120} unit="W" disabled={paramsLocked} onChange={v => { update("cpuLongPptW")(v); queueSmu("power_limit", v); }} />
      <Slider label="短时功耗" value={uxtuParams.cpuShortPptW} min={15} max={140} unit="W" disabled={paramsLocked} onChange={v => { update("cpuShortPptW")(v); queueSmu("short_power_limit", v); }} />
    </div>}

    {showGpu && <div className="card" style={{ padding: 20 }}>
      <div className="head" style={{ marginBottom: 18 }}>
        <span className="t"><span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="2" y="6" width="20" height="12" rx="1.5"/><circle cx="9" cy="12" r="3"/><path d="M16 10h3M16 14h3"/></svg></span>GPU 调节{customLabel}</span>
      </div>
      {gpuClockDisabled && <p style={{ marginBottom: 12, color: "var(--fg-3)", fontSize: 12, lineHeight: 1.5 }}>{gpuMode === 0 ? "混合模式下 GPU 时钟由系统管理，核心/显存频率设置不生效" : "集显模式下独显不可用，GPU 设置不生效"}</p>}
      <Slider label="核心频率" value={uxtuParams.gpuCoreFreqMhz} min={1000} max={3100} step={50} unit="MHz" disabled={gpuClockDisabled || paramsLocked} onChange={v => { update("gpuCoreFreqMhz")(v); queueGpuCore(v); }} action={<button onClick={toggleGpuLock} disabled={gpuClockDisabled || paramsLocked} style={{ marginLeft: 8, padding: "3px 10px", borderRadius: 6, border: "1px solid var(--stroke)", background: uxtuParams.gpuFreqLimitEnabled ? "var(--ok)" : "transparent", color: uxtuParams.gpuFreqLimitEnabled ? "var(--ok-fg)" : "var(--fg-2)", cursor: (gpuClockDisabled || paramsLocked) ? "not-allowed" : "pointer", fontSize: 11, fontFamily: "inherit", opacity: (gpuClockDisabled || paramsLocked) ? .5 : 1 }}>{uxtuParams.gpuFreqLimitEnabled ? "已锁定" : "锁定频率"}</button>} />
      <Slider label="核心偏移" value={uxtuParams.ocCoreOffsetMhz ?? 0} min={-200} max={300} step={25} unit="MHz" displayValue={((uxtuParams.ocCoreOffsetMhz ?? 0) >= 0 ? "+" : "") + (uxtuParams.ocCoreOffsetMhz ?? 0)} disabled={gpuAllDisabled || paramsLocked} onChange={v => { update("ocCoreOffsetMhz")(v); queueOc(); }} />
      <Slider label="显存频率" value={uxtuParams.gpuMemFreqMhz} min={0} max={3} step={1} unit="" displayValue={["自动", "9001", "11001", "12001"][uxtuParams.gpuMemFreqMhz] || ""} disabled={gpuClockDisabled || paramsLocked} onChange={v => { update("gpuMemFreqMhz")(v); queueGpuMem(v); }} />
      <Slider label="温度限制" value={uxtuParams.gpuTempLimitC ?? 87} min={60} max={100} unit="°C" disabled={gpuAllDisabled || paramsLocked} onChange={v => { update("gpuTempLimitC")(v); queueThermal(v); }} />
    </div>}</>);
}