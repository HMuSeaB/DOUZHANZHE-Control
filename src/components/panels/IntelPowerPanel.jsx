import { useCallback, useRef, useEffect } from "react";
import { applySmuSet } from "../../services/uxtuAdapter";
import OverrideSlider from "../ui/OverrideSlider";

export default function IntelPowerPanel({
  settings, uxtuParams, setUxtuParams, overrides, saveOverride, clearOverride, switching,
}) {
  const latestModeRef = useRef(settings.mode);
  latestModeRef.current = settings.mode;
  const paramsLocked = !!switching;
  const isSet = (key) => Object.prototype.hasOwnProperty.call(overrides, key);
  const update = useCallback((key) => (value) => {
    setUxtuParams(p => ({ ...p, [key]: value }));
    saveOverride?.(settings.mode, key, value);
  }, [setUxtuParams, saveOverride, settings.mode]);
  const smuTimer = useRef(null);
  useEffect(() => () => clearTimeout(smuTimer.current), []);
  function queueSmu(parameter, valueM) {
    clearTimeout(smuTimer.current);
    smuTimer.current = setTimeout(async () => {
      try { await applySmuSet(parameter, valueM, latestModeRef.current); }
      catch (err) { console.error("SMU set failed:", err); }
    }, 600);
  }
  return (
    <>
      <OverrideSlider
        label="长时功耗 (PL1)" desc="持续运行时的最大CPU功耗上限（PL1），影响长时间负载性能" value={uxtuParams.cpuLongPptW} min={15} max={120} step={1} unit="W"
        set={isSet('cpuLongPptW')} disabled={paramsLocked}
        onEnable={() => { update('cpuLongPptW')(uxtuParams.cpuLongPptW); queueSmu('power_limit', uxtuParams.cpuLongPptW); }}
        onClear={() => clearOverride(settings.mode, ['cpuLongPptW'])}
        onChange={v => { update('cpuLongPptW')(v); queueSmu('power_limit', v); }}
      />
      <OverrideSlider
        label="短时功耗 (PL2)" desc="短时突发的最大CPU功耗上限（PL2），影响短时间加速性能" value={uxtuParams.cpuShortPptW} min={15} max={140} step={1} unit="W"
        set={isSet('cpuShortPptW')} disabled={paramsLocked}
        onEnable={() => { update('cpuShortPptW')(uxtuParams.cpuShortPptW); queueSmu('short_power_limit', uxtuParams.cpuShortPptW); }}
        onClear={() => clearOverride(settings.mode, ['cpuShortPptW'])}
        onChange={v => { update('cpuShortPptW')(v); queueSmu('short_power_limit', v); }}
      />
    </>
  );
}
