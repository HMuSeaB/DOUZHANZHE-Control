import { useCallback, useRef, useEffect } from "react";
import { applySmuSet } from "../../services/uxtuAdapter";
import Card from "../ui/Card";
import SliderRow from "../ui/SliderRow";

export default function IntelPowerPanel({
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

  const smuTimer = useRef(null);
  useEffect(() => {
    return () => clearTimeout(smuTimer.current);
  }, []);

  function queueSmu(parameter, valueM) {
    clearTimeout(smuTimer.current);
    smuTimer.current = setTimeout(async () => {
      try { await applySmuSet(parameter, valueM, latestModeRef.current); }
      catch (err) { console.error("SMU set failed:", err); }
    }, 600);
  }

  return (
    <Card title={"CPU 功耗与温度" + (customLabel || "")} className="!p-3">
      <div className="space-y-3">
        <SliderRow label="长时功耗 (PL1)" value={uxtuParams.cpuLongPptW}
          min={15} max={120} unit="W"
          isCustom={isC("cpuLongPptW")}
          onChange={(v) => { update("cpuLongPptW")(v); queueSmu("power_limit", v); }} disabled={paramsLocked} />
        <SliderRow label="短时功耗 (PL2)" value={uxtuParams.cpuShortPptW}
          min={15} max={140} unit="W"
          isCustom={isC("cpuShortPptW")}
          onChange={(v) => { update("cpuShortPptW")(v); queueSmu("short_power_limit", v); }} disabled={paramsLocked} />
      </div>
    </Card>
  );
}
