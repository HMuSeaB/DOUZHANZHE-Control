import { useState, useEffect, useCallback, useRef } from "react";
import {
  applyHardwareControl,
  powerPlanHALMap,
  setCpuFreqLimit,
  setCpuCoreLimitPercent,
} from "../../services/uxtuAdapter";
import Card from "../ui/Card";
import SliderRow from "../ui/SliderRow";
import SwitchRow from "../ui/SwitchRow";

const POWER_PLANS = [
  { id: "efficiency", label: "最高能效", halValue: powerPlanHALMap.efficiency },
  { id: "balance", label: "平衡", halValue: powerPlanHALMap.balance },
  { id: "performance", label: "最佳性能", halValue: powerPlanHALMap.performance },
];

export default function IntelCpuPanel({
  settings, uxtuParams, setUxtuParams,
  overrides, saveOverride, switching, customLabel,
}) {
  const latestModeRef = useRef(settings.mode);
  latestModeRef.current = settings.mode;

  const paramsLocked = !!switching;

  const update = useCallback((key) => (value) => {
    setUxtuParams(p => ({ ...p, [key]: value }));
    saveOverride?.(settings.mode, key, value);
  }, [setUxtuParams, saveOverride, settings.mode]);

  const isC = useCallback((key) => (overrides ? key in overrides : undefined), [overrides]);

  const cpuFreqTimer = useRef(null);
  const coreTimer = useRef(null);

  useEffect(() => {
    return () => {
      clearTimeout(cpuFreqTimer.current);
      clearTimeout(coreTimer.current);
    };
  }, []);

  function queueCpuFreq(mhz) {
    clearTimeout(cpuFreqTimer.current);
    cpuFreqTimer.current = setTimeout(async () => {
      try { await setCpuFreqLimit(mhz, latestModeRef.current); }
      catch (err) { console.error("CPU freq-limit failed:", err); }
    }, 600);
  }

  function queueCoreLimit(coreCount) {
    clearTimeout(coreTimer.current);
    coreTimer.current = setTimeout(async () => {
      try {
        const percent = coreCount > 0 ? Math.round(coreCount / 16 * 100) : 100;
        await setCpuCoreLimitPercent(percent, latestModeRef.current);
      } catch (err) { console.error("Core limit failed:", err); }
    }, 600);
  }

  return (
    <Card title={"CPU 频率控制" + (customLabel || "")} className="!p-3">
      <div className="space-y-3">
        <SwitchRow label="频率限制" checked={uxtuParams.cpuFreqLimitEnabled}
          isCustom={isC("cpuFreqLimitEnabled")}
          onChange={(on) => { update("cpuFreqLimitEnabled")(on); queueCpuFreq(on ? uxtuParams.cpuFreqLimitMhz : 0); }}
          disabled={paramsLocked} />
        {uxtuParams.cpuFreqLimitEnabled && (
          <SliderRow label="最大频率" value={uxtuParams.cpuFreqLimitMhz}
            min={2000} max={5500} step={100} unit="MHz"
            isCustom={isC("cpuFreqLimitMhz")}
            onChange={(v) => { update("cpuFreqLimitMhz")(v); queueCpuFreq(v); }} />
        )}
        <SwitchRow label="限制核心数" checked={uxtuParams.cpuCoreLimit > 0}
          isCustom={isC("cpuCoreLimit")}
          onChange={(on) => { const v = on ? 8 : 0; update("cpuCoreLimit")(v); queueCoreLimit(v); }}
          disabled={paramsLocked} />
        {uxtuParams.cpuCoreLimit > 0 && (
          <SliderRow label="核心数" value={uxtuParams.cpuCoreLimit}
            min={2} max={14} step={2} unit="核"
            isCustom={isC("cpuCoreLimit")}
            onChange={(v) => { update("cpuCoreLimit")(v); queueCoreLimit(v); }} disabled={paramsLocked} />
        )}
        <div>
          <p className="text-xs mb-1" style={{ color: "var(--muted)" }}>电源管理</p>
          <div className="flex gap-1">
            {POWER_PLANS.map((plan) => (
              <button key={plan.id} onClick={() => {
                update("cpuPowerPlan")(plan.id);
                if (plan.halValue !== undefined) applyHardwareControl("power_plan", plan.halValue, settings.mode).catch(() => {});
              }}
                disabled={paramsLocked}
                className="text-xs px-2 py-1 rounded-lg"
                style={{ border: "1px solid var(--border)", background: uxtuParams.cpuPowerPlan === plan.id ? "var(--primary)" : "var(--card-2)", color: uxtuParams.cpuPowerPlan === plan.id ? "#fff" : "var(--text)" }}
              >{plan.label}</button>
            ))}
          </div>
        </div>
      </div>
    </Card>
  );
}
