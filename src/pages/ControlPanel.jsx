import { useState, useEffect } from "react";
import { useControlState } from "../hooks/useControlState";
import PerformancePanel from "../components/panels/PerformancePanel";
import IntelCpuPanel from "../components/panels/IntelCpuPanel";
import IntelPowerPanel from "../components/panels/IntelPowerPanel";
import AmdCpuPanel from "../components/panels/AmdCpuPanel";
import AmdPowerPanel from "../components/panels/AmdPowerPanel";

export default function ControlPanel() {
  const { telemetry, settings, setSettings, uxtuParams, setUxtuParams, overrides, saveOverride, switching } = useControlState();
  const [cpuVendor, setCpuVendor] = useState(null);
  const gpuMode = telemetry?.gpuMode ?? null;

  useEffect(() => {
    fetch('/api/platform/info')
      .then(r => r.json())
      .then(d => setCpuVendor(d.vendor))
      .catch(() => setCpuVendor('unknown'));
  }, []);

  const panelProps = { settings, setSettings, uxtuParams, setUxtuParams, overrides, saveOverride, switching };

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>控制面板</h1>
          <p>CPU / GPU 参数调节 · 实时生效</p>
        </div>
      </div>
      <div className="grid">
        {cpuVendor === 'AMD' ? (<><div className="reveal enter" style={{animationDelay:".04s"}}><AmdCpuPanel {...panelProps} /></div><div className="reveal enter" style={{animationDelay:".08s"}}><AmdPowerPanel {...panelProps} /></div></>)
          : cpuVendor === null ? null
          : (<><div className="reveal enter" style={{animationDelay:".04s"}}><IntelCpuPanel {...panelProps} /></div><div className="reveal enter" style={{animationDelay:".08s"}}><IntelPowerPanel {...panelProps} /></div></>)}
        <div className="reveal enter" style={{ animationDelay: ".12s" }}>
          <PerformancePanel {...panelProps} showCpu={false} showPower={false} showGpu={true} gpuMode={gpuMode} />
        </div>
      </div>
    </section>
  );
}