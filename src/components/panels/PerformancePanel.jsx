import { useState, useEffect, useCallback, useRef } from "react";
import { applySmuSet, applyHardwareControl, powerPlanHALMap, applyGpuControl, applyNvapiOverclock, applyNvapiThermalLimit, setCpuFreqLimit, setCpuTurbo, setCpuCoreLimitPercent, coreToPercent } from "../../services/uxtuAdapter";
import { useToast } from "../ui/Toast";
import OverrideSlider from "../ui/OverrideSlider";

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
  overrides, saveOverride, clearOverride, gpuMode, switching,
}) {
  const toast = useToast();
  const gpuClockDisabled = gpuMode === 0 || gpuMode === 2;
  const gpuAllDisabled = gpuMode === 2;
  const paramsLocked = !!switching;
  const isSet = (key) => Object.prototype.hasOwnProperty.call(overrides, key);
  const gpuCoreSet = isSet('gpuCoreFreqMhz') || isSet('gpuFreqLimitEnabled');
  const gpuMemSet = isSet('gpuMemFreqMhz');
  const gpuOffsetSet = isSet('ocCoreOffsetMhz');
  const gpuTempSet = isSet('gpuTempLimitC');
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
  function queueOc() { clearTimeout(ocTimer.current); ocTimer.current = setTimeout(async () => { try { const p = latestParamsRef.current; await applyNvapiOverclock(p.ocCoreOffsetMhz ?? 0, p.ocMemOffsetMhz ?? 0, latestModeRef.current); } catch (err) { console.error('OC failed:', err); } }, 600); }
  function queueGpuMem(level) {
    clearTimeout(gpuMemTimer.current);
    const memMap = [0, 9001, 11001, 12001];
    const target = memMap[level] ?? 0;
    gpuMemTimer.current = setTimeout(async () => {
      const mode = latestModeRef.current;
      try {
        await applyGpuControl('reset-memory-clocks', undefined, undefined, undefined, mode).catch(() => {});
        if (target > 0) await applyGpuControl('limit-memory', target, undefined, undefined, mode);
      } catch (err) { console.error('GPU mem failed:', err); }
    }, 600);
  }
  function queueThermal(limit) { clearTimeout(thermalTimer.current); thermalTimer.current = setTimeout(async () => { try { await applyNvapiThermalLimit(limit, latestModeRef.current); } catch (err) { console.error('Thermal limit failed:', err); } }, 600); }
  function queueCoreLimit(coreCount) { clearTimeout(coreTimer.current); coreTimer.current = setTimeout(async () => { try { await setCpuCoreLimitPercent(coreToPercent(coreCount), latestModeRef.current); } catch (err) { console.error('Core limit failed:', err); } }, 600); }
  return (<>
    {showCpu && <>
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
    </>}

    {showPower && <>
      <Slider label="温度墙" value={uxtuParams.cpuTempLimitC} min={60} max={100} unit="°C" disabled={paramsLocked} onChange={v => { update("cpuTempLimitC")(v); queueSmu("temp_limit", v); }} />
      <Slider label="电压调节(降压)" value={uxtuParams.cpuVoltageOffset} min={-30} max={0} step={1} unit="mV" disabled={paramsLocked} onChange={v => { update("cpuVoltageOffset")(v); queueSmu("co_all", v); }} />
      <Slider label="长时功耗" value={uxtuParams.cpuLongPptW} min={15} max={120} unit="W" disabled={paramsLocked} onChange={v => { update("cpuLongPptW")(v); queueSmu("power_limit", v); }} />
      <Slider label="短时功耗" value={uxtuParams.cpuShortPptW} min={15} max={140} unit="W" disabled={paramsLocked} onChange={v => { update("cpuShortPptW")(v); queueSmu("short_power_limit", v); }} />
    </>}

    {showGpu && <>
      {gpuClockDisabled && <p style={{ marginBottom: 12, color: "var(--fg-3)", fontSize: 12, lineHeight: 1.5 }}>{gpuMode === 0 ? "混合模式下 GPU 时钟由系统管理，核心/显存频率设置不生效" : "集显模式下独显不可用，GPU 设置不生效"}</p>}
      <OverrideSlider
        label="核心频率" desc="GPU核心运行的最大频率，影响图形渲染和计算性能" value={uxtuParams.gpuCoreFreqMhz} min={1000} max={3100} step={50} unit="MHz"
        set={gpuCoreSet} disabled={gpuClockDisabled || paramsLocked}
        onEnable={() => {
          update('gpuCoreFreqMhz')(uxtuParams.gpuCoreFreqMhz);
          update('gpuFreqLimitEnabled')(uxtuParams.gpuFreqLimitEnabled);
          update('gpuFreqLimitMhz')(uxtuParams.gpuFreqLimitMhz);
          queueGpuCore(uxtuParams.gpuCoreFreqMhz);
        }}
        onClear={async () => {
          if (uxtuParams.gpuFreqLimitEnabled) {
            await gpuCmd('reset-clocks').catch(() => {});
          }
          clearOverride(settings.mode, ['gpuCoreFreqMhz', 'gpuFreqLimitEnabled', 'gpuFreqLimitMhz']);
        }}
        onChange={v => { update('gpuCoreFreqMhz')(v); queueGpuCore(v); }}
        action={<button onClick={toggleGpuLock} disabled={gpuClockDisabled || paramsLocked || !gpuCoreSet} style={{ marginLeft: 8, padding: "3px 10px", borderRadius: 6, border: "1px solid var(--stroke)", background: uxtuParams.gpuFreqLimitEnabled ? "var(--ok)" : "transparent", color: uxtuParams.gpuFreqLimitEnabled ? "var(--ok-fg)" : "var(--fg-2)", cursor: (gpuClockDisabled || paramsLocked || !gpuCoreSet) ? "not-allowed" : "pointer", fontSize: 11, fontFamily: "inherit", opacity: (gpuClockDisabled || paramsLocked || !gpuCoreSet) ? .5 : 1 }}>{uxtuParams.gpuFreqLimitEnabled ? "已锁定" : "锁定频率"}</button>}
      />
      <OverrideSlider
        label="核心偏移" desc="在默认频率基础上偏移的MHz值，正值超频负值降频" value={uxtuParams.ocCoreOffsetMhz ?? 0} min={-200} max={300} step={25} unit="MHz"
        displayValue={((uxtuParams.ocCoreOffsetMhz ?? 0) >= 0 ? "+" : "") + (uxtuParams.ocCoreOffsetMhz ?? 0)}
        set={gpuOffsetSet} disabled={gpuAllDisabled || paramsLocked}
        onEnable={() => { update('ocCoreOffsetMhz')(uxtuParams.ocCoreOffsetMhz ?? 0); update('ocMemOffsetMhz')(uxtuParams.ocMemOffsetMhz ?? 0); queueOc(); }}
        onClear={() => clearOverride(settings.mode, ['ocCoreOffsetMhz', 'ocMemOffsetMhz'])}
        onChange={v => { update('ocCoreOffsetMhz')(v); queueOc(); }}
      />
      <OverrideSlider
        label="显存频率" desc="显存运行频率等级，等级越高显存带宽越大" value={uxtuParams.gpuMemFreqMhz} min={0} max={3} step={1} unit=""
        displayValue={["自动", "9001", "11001", "12001"][uxtuParams.gpuMemFreqMhz] || ""}
        set={gpuMemSet} disabled={gpuClockDisabled || paramsLocked}
        onEnable={() => { update('gpuMemFreqMhz')(uxtuParams.gpuMemFreqMhz); queueGpuMem(uxtuParams.gpuMemFreqMhz); }}
        onClear={() => clearOverride(settings.mode, ['gpuMemFreqMhz'])}
        onChange={v => { update('gpuMemFreqMhz')(v); queueGpuMem(v); }}
      />
      <OverrideSlider
        label="温度限制" desc="GPU温度达到此值后会自动降频保护" value={uxtuParams.gpuTempLimitC ?? 87} min={60} max={100} unit="°C"
        set={gpuTempSet} disabled={gpuAllDisabled || paramsLocked}
        onEnable={() => { update('gpuTempLimitC')(uxtuParams.gpuTempLimitC ?? 87); queueThermal(uxtuParams.gpuTempLimitC ?? 87); }}
        onClear={() => clearOverride(settings.mode, ['gpuTempLimitC'])}
        onChange={v => { update('gpuTempLimitC')(v); queueThermal(v); }}
      />
    </>}</>);
}
