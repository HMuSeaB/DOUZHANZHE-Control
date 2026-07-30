import { useControlState } from "../hooks/useControlState";
import PerformancePanel from "../components/panels/PerformancePanel";
import IntelCpuPanel from "../components/panels/IntelCpuPanel";
import IntelPowerPanel from "../components/panels/IntelPowerPanel";

export default function ControlPanel() {
  const {
    telemetry, settings, setSettings,
    uxtuParams, setUxtuParams,
    overrides, saveOverride, switching,
  } = useControlState();

  const gpuMode = telemetry?.gpuMode ?? null;

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>控制面板</h1>
          <p>CPU / GPU 参数调节 · 实时生效</p>
        </div>
      </div>

      <div className="grid" style={{ maxWidth: 800 }}>
        <div className="reveal enter" style={{ animationDelay: ".04s" }}>
          <IntelCpuPanel
            settings={settings}
            uxtuParams={uxtuParams}
            setUxtuParams={setUxtuParams}
            overrides={overrides}
            saveOverride={saveOverride}
            switching={switching}
          />
        </div>

        <div className="reveal enter" style={{ animationDelay: ".08s" }}>
          <IntelPowerPanel
            settings={settings}
            uxtuParams={uxtuParams}
            setUxtuParams={setUxtuParams}
            overrides={overrides}
            saveOverride={saveOverride}
            switching={switching}
          />
        </div>

        <div className="reveal enter" style={{ animationDelay: ".12s" }}>
          <PerformancePanel
            settings={settings}
            setSettings={setSettings}
            uxtuParams={uxtuParams}
            setUxtuParams={setUxtuParams}
            overrides={overrides}
            saveOverride={saveOverride}
            switching={switching}
            showCpu={false}
            showPower={false}
            showGpu={true}
            gpuMode={gpuMode}
          />
        </div>
      </div>
    </section>
  );
}
