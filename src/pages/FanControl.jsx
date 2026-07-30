import { useControlState } from "../hooks/useControlState";
import FanCurvePanel from "../components/panels/FanCurvePanel";

export default function FanControl() {
  const { telemetry, settings, overrides } = useControlState();

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>风扇控制</h1>
          <p>EC 寄存器绑定 · 自定义风扇曲线 · 手动调速</p>
        </div>
      </div>

      <div className="reveal enter" style={{ animationDelay: ".04s" }}>
        <FanCurvePanel
          telemetry={telemetry}
          overrides={overrides}
          settings={settings}
        />
      </div>
    </section>
  );
}
