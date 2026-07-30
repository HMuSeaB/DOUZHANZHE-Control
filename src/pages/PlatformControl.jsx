import { useControlState } from "../hooks/useControlState";
import { useState, useEffect } from "react";
import SwitchRow from "../components/ui/SwitchRow";

export default function PlatformControl() {
  const { settings, setSettings, telemetry } = useControlState();
  const [kbBrightness, setKbBrightness] = useState(0);

  useEffect(() => {
    fetch("/api/platform/info")
      .then(r => r.json())
      .then(d => {
        if (d.kbBrightnessLevel != null) setKbBrightness(d.kbBrightnessLevel);
      })
      .catch(() => {});
  }, []);

  const updateKbBrightness = (v) => {
    setKbBrightness(v);
    fetch("/api/platform/kb-brightness", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ level: v }),
    }).catch(() => {});
  };

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>平台控制</h1>
          <p>笔记本型号绑定的 EC / WMI 控制</p>
        </div>
      </div>

      <div className="grid" style={{ maxWidth: 600, gap: 16 }}>
        {/* GPU 模式 */}
        <div className="card reveal enter" style={{ animationDelay: ".04s" }}>
          <div className="head" style={{ marginBottom: 14 }}>
            <span className="t">
              <span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="2" y="6" width="20" height="12" rx="1.5"/><circle cx="9" cy="12" r="3"/><path d="M16 10h3M16 14h3"/></svg></span>
              GPU 模式
            </span>
          </div>
          <div style={{ display: "flex", gap: 8 }}>
            {[
              { id: 2, label: "集显", desc: "节能模式" },
              { id: 0, label: "混合", desc: "自动切换" },
              { id: 1, label: "独显", desc: "高性能" },
            ].map(m => (
              <button
                key={m.id}
                className={`mode-btn${telemetry?.gpuMode === m.id ? " active" : ""}`}
                style={{ flex: 1 }}
                onClick={() => {
                  fetch("/api/platform/gpu-mode", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ mode: m.id }),
                  }).catch(() => {});
                }}
              >
                <span className="txt">
                  <b>{m.label}</b>
                  <small>{m.desc}</small>
                </span>
              </button>
            ))}
          </div>
          <div className="metric-row" style={{ marginTop: 14 }}>
            <span className="k">当前模式</span>
            <span className="v">
              {telemetry?.gpuMode === 1 ? "独显" : telemetry?.gpuMode === 0 ? "混合" : telemetry?.gpuMode === 2 ? "集显" : "未知"}
            </span>
          </div>
        </div>

        {/* 键盘背光 */}
        <div className="card reveal enter" style={{ animationDelay: ".08s" }}>
          <div className="head" style={{ marginBottom: 14 }}>
            <span className="t">
              <span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="4" y="8" width="16" height="8" rx="1.5"/><path d="M7 11h2M11 11h2M15 11h2M9 14h6"/></svg></span>
              键盘背光
            </span>
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <span className="k" style={{ fontSize: 13, color: "var(--fg-3)", minWidth: 50 }}>亮度</span>
            <div style={{ flex: 1, display: "flex", gap: 6 }}>
              {[0, 1, 2, 3].map(lvl => (
                <button
                  key={lvl}
                  className="text-xs px-3 py-1.5 rounded-lg transition-all"
                  style={{
                    flex: 1,
                    border: kbBrightness === lvl ? "1px solid var(--primary)" : "1px solid var(--stroke)",
                    background: kbBrightness === lvl ? "color-mix(in srgb, var(--primary) 20%, var(--surface))" : "transparent",
                    color: kbBrightness === lvl ? "var(--primary)" : "var(--fg-2)",
                    cursor: "pointer",
                  }}
                  onClick={() => updateKbBrightness(lvl)}
                >
                  {["关", "低", "中", "高"][lvl]}
                </button>
              ))}
            </div>
          </div>
        </div>

        {/* 系统开关 */}
        <div className="card reveal enter" style={{ animationDelay: ".12s" }}>
          <div className="head" style={{ marginBottom: 14 }}>
            <span className="t">
              <span className="chip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="8" width="18" height="8" rx="1.5"/><path d="M8 2v4M16 2v4M3 14h18"/></svg></span>
              系统开关
            </span>
          </div>
          <div className="space-y-2">
            <SwitchRow
              label="Num Lock"
              checked={settings.numLock ?? true}
              onChange={(v) => {
                setSettings(prev => ({ ...prev, numLock: v }));
                fetch("/api/platform/numlock", {
                  method: "POST",
                  headers: { "Content-Type": "application/json" },
                  body: JSON.stringify({ enabled: v }),
                }).catch(() => {});
              }}
            />
            <SwitchRow
              label="Caps Lock"
              checked={settings.capsLock ?? false}
              onChange={(v) => {
                setSettings(prev => ({ ...prev, capsLock: v }));
                fetch("/api/platform/capslock", {
                  method: "POST",
                  headers: { "Content-Type": "application/json" },
                  body: JSON.stringify({ enabled: v }),
                }).catch(() => {});
              }}
            />
            <SwitchRow
              label="Fn Lock"
              checked={settings.fnLock ?? false}
              onChange={(v) => {
                setSettings(prev => ({ ...prev, fnLock: v }));
                fetch("/api/platform/fnlock", {
                  method: "POST",
                  headers: { "Content-Type": "application/json" },
                  body: JSON.stringify({ enabled: v }),
                }).catch(() => {});
              }}
            />
            <SwitchRow
              label="触控板锁定"
              checked={settings.touchpadLock ?? false}
              onChange={(v) => {
                setSettings(prev => ({ ...prev, touchpadLock: v }));
                fetch("/api/platform/touchpad", {
                  method: "POST",
                  headers: { "Content-Type": "application/json" },
                  body: JSON.stringify({ locked: v }),
                }).catch(() => {});
              }}
            />
          </div>
        </div>
      </div>
    </section>
  );
}
