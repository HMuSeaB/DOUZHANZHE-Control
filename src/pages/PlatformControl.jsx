import { useControlState } from "../hooks/useControlState";
import { applyHardwareControl, log } from "../services/uxtuAdapter";

export default function PlatformControl() {
  const { telemetry } = useControlState();

  const asBool = (value) => value === true || value === 1 || value === "1";
  const kbLevel = telemetry?.kbBrightness != null ? Number(telemetry.kbBrightness) : 0;
  const gpuMode = telemetry?.savedGpuMode != null
    ? Number(telemetry.savedGpuMode)
    : telemetry?.gpuMode != null
      ? Number(telemetry.gpuMode)
      : null;

  const setKb = (v) => {
    applyHardwareControl("kb_light", Number(v))
      .catch(err => log("PlatformControl", `kb_light 设置失败: ${err.message}`));
  };

  const setSwitch = (target, val) => {
    applyHardwareControl(target, val ? 1 : 0)
      .catch(err => log("PlatformControl", `${target} 设置失败: ${err.message}`));
  };

  const gpuModes = [
    { id: 2, label: "集显", desc: "节能模式" },
    { id: 0, label: "混合", desc: "自动切换" },
    { id: 1, label: "独显", desc: "高性能" },
  ];

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>平台控制</h1>
          <p>EC / WMI 型号绑定控制 · 后端自动检测 · 动态渲染可用项</p>
        </div>
      </div>

      <div className="hint" style={{ margin: "0 0 18px" }}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><circle cx="12" cy="12" r="9"/><path d="M12 8v5M12 16h.01"/></svg>
        本页内容随笔记本型号变化：后端返回什么控制项，前端就渲染什么。
      </div>

      <div className="section-title">控制项<span className="line"></span></div>
      <div className="card reveal enter" style={{ animationDelay: ".02s" }}>
        <div className="grid2">
          <div className="g-cell">
            <span className="rk"><b>键盘灯亮度</b><small>EC 寄存器控制键盘背光 · 0–3 档</small></span>
            <span className="g-ctrl">
              <input type="range" className="slider" min="0" max="3" value={kbLevel} onChange={e => setKb(Number(e.target.value))} style={{ width: '60%' }} />
              <span className="pv">{kbLevel === 0 ? '关' : kbLevel + ' 档'}</span>
            </span>
          </div>
          <div className="g-cell">
            <span className="rk"><b>GPU 模式</b><small>混合 / 独显 / 集显 · WMI · 切换需重启</small></span>
            <span className="g-ctrl">
              <div className="segmented">
                {gpuModes.map(m => (
                  <button key={m.id} className={gpuMode === m.id ? "active" : ""}
                    onClick={() => applyHardwareControl("gpu_mode", m.id)
                      .catch(err => log("PlatformControl", `gpu_mode 设置失败: ${err.message}`))}>
                    {m.label}
                  </button>
                ))}
              </div>
            </span>
          </div>
        </div>
      </div>

      <div className="section-title">键盘按键<span className="tag">OEM EC</span><span className="line"></span></div>
      <div className="card reveal enter keys-card" style={{ animationDelay: ".04s" }}>
        <div className="grid2">
          <div className="g-cell">
            <span className="rk"><b>FN 锁</b><small>锁定 Fn 键行为 · F1–F12 与多媒体键互换</small></span>
            <span className="g-ctrl">
              <label className="switch"><input type="checkbox" checked={asBool(telemetry?.fnLock)} onChange={e => setSwitch("fn_lock", e.target.checked)} /><span className="track"></span></label>
            </span>
          </div>
          <div className="g-cell">
            <span className="rk"><b>大写锁定</b><small>锁定 Caps Lock 键 · 防游戏中误触</small></span>
            <span className="g-ctrl">
              <label className="switch"><input type="checkbox" checked={asBool(telemetry?.capsLock)} onChange={e => setSwitch("caps_lock", e.target.checked)} /><span className="track"></span></label>
            </span>
          </div>
          <div className="g-cell">
            <span className="rk"><b>数字小键盘锁</b><small>锁定数字小键盘 · 防误触或切换为方向键功能</small></span>
            <span className="g-ctrl">
              <label className="switch"><input type="checkbox" checked={asBool(telemetry?.numLock)} onChange={e => setSwitch("num_lock", e.target.checked)} /><span className="track"></span></label>
            </span>
          </div>
          <div className="g-cell">
            <span className="rk"><b>触控板锁</b><small>禁用触控板 · 外接鼠标时防误触</small></span>
            <span className="g-ctrl">
              <label className="switch"><input type="checkbox" checked={asBool(telemetry?.touchpadLock)} onChange={e => setSwitch("touchpad_lock", e.target.checked)} /><span className="track"></span></label>
            </span>
          </div>
        </div>
      </div>

      <div className="section-title">EC 信息<span className="tag">只读</span><span className="line"></span></div>
      <div className="card reveal enter" style={{ padding: 0, animationDelay: ".06s" }}>
        <div className="row-line"><span className="rk"><b>EC 固件版本</b><small>嵌入式控制器固件</small></span><span className="pv" style={{ width: "auto" }}>—</span></div>
        <div className="row-line"><span className="rk"><b>CPU 传感器原始值</b><small>EC 直读 · 未校准</small></span><span className="pv" style={{ width: "auto" }}>—</span></div>
        <div className="row-line"><span className="rk"><b>GPU 传感器原始值</b><small>EC 直读 · 未校准</small></span><span className="pv" style={{ width: "auto" }}>—</span></div>
        <div className="row-line"><span className="rk"><b>主板温度</b><small>EC 直读</small></span><span className="pv" style={{ width: "auto" }}>—</span></div>
      </div>
    </section>
  );
}
