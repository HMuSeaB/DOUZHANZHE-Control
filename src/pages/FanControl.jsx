import { useState, useEffect, useCallback, useRef } from "react";
import { useControlState } from "../hooks/useControlState";
import FanCurvePanel from "../components/panels/FanCurvePanel";
import { fetchFanCurveStatus, fetchRouteInfo, getFanRange, MODE_FAN_DEFAULTS, resolvePerfMode } from "../services/uxtuAdapter";

const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

// ─────────────────────────────────────────────────────────────────────────────
// 手动调速滑块的值：唯一权威源 = 后端 overrides（/api/overrides 的稀疏覆盖项）。
//
// 旧实现用本地 useState 存滑块值，只从 MODE_FAN_DEFAULTS 取初值，并有一个
// useEffect([perfMode]) 在模式变化时把滑块重置为模式默认值 —— 从不读回后端
// 已持久化的 fan.largeRpm/smallRpm。后果：游戏自动切换 / 热键切档 / 配置栏
// 切配置 / 重开应用 时，滑块都会「刷新并恢复默认」，用户设的转速被冲掉。
//
// 现在：后端有覆盖 → 显示覆盖值（带「自定义」标记）；没有覆盖 → 才回落模式
// 官方默认。拖动时同步写 store + 防抖 POST /api/fan/set-target 落盘。
// ─────────────────────────────────────────────────────────────────────────────
export default function FanControl() {
  const { telemetry, settings, overrides, profiles, saveOverride, clearOverride } = useControlState();
  const [curveActive, setCurveActive] = useState(false);
  const [routeInfo, setRouteInfo] = useState(null);

  // 风扇区间/默认按「性能模式」取，配置 id 先解包成性能模式裸名
  const perfMode = resolvePerfMode(settings.mode, profiles);
  const fanRange = getFanRange(perfMode);
  const fanDefaults = MODE_FAN_DEFAULTS[perfMode] || MODE_FAN_DEFAULTS.office;

  const fan1IsCustom = overrides?.fanLargeRpmTarget != null;
  const fan2IsCustom = overrides?.fanSmallRpmTarget != null;
  // 越界覆盖（改过 profile 的 thermalMode 等场景）只做显示钳位，不主动改写用户配置
  const fan1TargetRpm = clamp(overrides?.fanLargeRpmTarget ?? fanDefaults.fanLargeRpmTarget, fanRange.largeMin, fanRange.largeMax);
  const fan2TargetRpm = clamp(overrides?.fanSmallRpmTarget ?? fanDefaults.fanSmallRpmTarget, fanRange.smallMin, fanRange.smallMax);

  // 实时转速显示兜底：后端已过滤 EC 脏数据，这里再挡一层，避免旧后端或脏负载把
  // 物理不可能的读数（实测出现过 25249 RPM，大扇上限 4400）直接摆到界面上。
  const rawFan1Rpm = telemetry?.fanLargeRpm ?? 0;
  const rawFan2Rpm = telemetry?.fanSmallRpm ?? 0;
  const fan1Valid = rawFan1Rpm > 0 && (!telemetry?.fanLargeMax || rawFan1Rpm <= telemetry.fanLargeMax);
  const fan2Valid = rawFan2Rpm > 0 && (!telemetry?.fanSmallMax || rawFan2Rpm <= telemetry.fanSmallMax);
  const fan1Rpm = fan1Valid ? rawFan1Rpm : 0;
  const fan2Rpm = fan2Valid ? rawFan2Rpm : 0;
  const fan1Pct = fan1Valid && telemetry?.fanLargeMax ? Math.min(100, (fan1Rpm / telemetry.fanLargeMax) * 100) : 0;
  const fan2Pct = fan2Valid && telemetry?.fanSmallMax ? Math.min(100, (fan2Rpm / telemetry.fanSmallMax) * 100) : 0;

  useEffect(() => {
    let disposed = false;
    const refresh = async () => {
      try {
        const [status, route] = await Promise.all([fetchFanCurveStatus(), fetchRouteInfo()]);
        if (disposed) return;
        if (status?.ok) setCurveActive(status.active);
        setRouteInfo(route);
      } catch { /* backend offline */ }
    };
    refresh();
    const timer = setInterval(refresh, 2000);
    return () => { disposed = true; clearInterval(timer); };
  }, []);

  // 每个风扇一条尾随防抖：拖动时 store 立即更新（UI 跟随），硬件写入合并成一次
  const writeTimers = useRef([null, null]);
  useEffect(() => () => {
    writeTimers.current.forEach((t) => t && clearTimeout(t));
    writeTimers.current = [null, null];
  }, []);

  const writeFanTarget = useCallback((fanIdx, rpm) => {
    saveOverride(settings.mode, fanIdx === 0 ? "fanLargeRpmTarget" : "fanSmallRpmTarget", rpm);
    if (writeTimers.current[fanIdx]) clearTimeout(writeTimers.current[fanIdx]);
    writeTimers.current[fanIdx] = setTimeout(() => {
      writeTimers.current[fanIdx] = null;
      const body = fanIdx === 0 ? { largeRpm: rpm } : { smallRpm: rpm };
      fetch(`/api/fan/set-target?mode=${encodeURIComponent(settings.mode)}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      }).catch(() => { /* 写入失败保持当前值，下次拖动重试 */ });
    }, 250);
  }, [settings.mode, saveOverride]);

  const resetFanTargets = useCallback(async () => {
    writeTimers.current.forEach((t, i) => {
      if (t) { clearTimeout(t); writeTimers.current[i] = null; }
    });
    try {
      // 后端 /api/overrides/clear 会清掉 fan.largeRpm/smallRpm 并退出 SetFanManual，
      // 让 EC 回到固件自动控制；store 同步删除覆盖项 → 滑块回落模式默认值
      await clearOverride(settings.mode, ["fanLargeRpmTarget", "fanSmallRpmTarget"]);
    } catch { /* 后端离线：保持现值 */ }
  }, [settings.mode, clearOverride]);

  const anyCustom = fan1IsCustom || fan2IsCustom;

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>风扇控制</h1>
          <p>EC 寄存器绑定 · 手动调速与自定义曲线互斥 · 仅斗战者机型可见</p>
        </div>
      </div>

      {routeInfo?.deviationAlert && (
        <div className="fan-alert reveal enter">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" width="18" height="18"><path d="M12 9v4M12 17h.01"/><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg>
          <span className="fa-text">
            <b>风扇偏离告警</b>
            <small>连续 {routeInfo.consecutiveDeviation} 次采样未达到目标转速：大风扇偏差 {routeInfo.largeDeviationRpm} RPM · 小风扇偏差 {routeInfo.smallDeviationRpm} RPM</small>
          </span>
        </div>
      )}

      {/* 实时监控 */}
      <div className="section-title">实时监控<span className="line"></span></div>
      <div className="card reveal enter" style={{ padding: "6px 20px", animationDelay: ".02s" }}>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>大风扇</span>
          <div className="bar"><i style={{ width: fan1Pct + "%" }}></i></div>
          <span className="rpm"><b>{fan1Valid ? fan1Rpm : "—"}</b> RPM<small>{fan1Valid ? `实时 ${Math.round(fan1Pct)}%` : "读数无效"}</small></span>
        </div>
        <div className="fan-row">
          <span className="fname"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="12" cy="12" r="2.4"/><path d="M12 9.6c0-3 1.5-5 4-5 1.5 2-.5 5-4 5Zm2.1 3.3c2.6 1.5 3.4 3.7 2.2 5.9-2.4.4-4-2.4-2.2-5.9Zm-6.3.1c-2.6 1.5-4.8.7-5.9-1.6 1.6-1.9 4.7-1 6 1.6Z"/></svg>小风扇</span>
          <div className="bar"><i style={{ width: fan2Pct + "%" }}></i></div>
          <span className="rpm"><b>{fan2Valid ? fan2Rpm : "—"}</b> RPM<small>{fan2Valid ? `实时 ${Math.round(fan2Pct)}%` : "读数无效"}</small></span>
        </div>
      </div>

      {/* 手动调速 */}
      <div className="section-title">手动调速<span className={"tag" + (curveActive ? "" : " hidden")}>曲线运行时禁用</span><span className="line"></span></div>
      <div className="card reveal enter" style={{ padding: "6px 20px 12px", animationDelay: ".06s" }}>
        <div className="param">
          <span className="pk">
            <b>大风扇目标{fan1IsCustom && <i className="fan-tag">自定义</i>}</b>
            <small>固定转速 · 当前模式 {fanRange.largeMin}–{fanRange.largeMax} RPM · 模式默认 {fanDefaults.fanLargeRpmTarget}</small>
          </span>
          <input type="range" className="slider fan-manual" min={fanRange.largeMin} max={fanRange.largeMax} step="100" value={fan1TargetRpm} disabled={curveActive}
            onChange={e => writeFanTarget(0, Number(e.target.value))} />
          <span className="pv">{fan1TargetRpm} <small>RPM</small></span>
        </div>
        <div className="param">
          <span className="pk">
            <b>小风扇目标{fan2IsCustom && <i className="fan-tag">自定义</i>}</b>
            <small>固定转速 · 当前模式 {fanRange.smallMin}–{fanRange.smallMax} RPM · 模式默认 {fanDefaults.fanSmallRpmTarget}</small>
          </span>
          <input type="range" className="slider fan-manual" min={fanRange.smallMin} max={fanRange.smallMax} step="100" value={fan2TargetRpm} disabled={curveActive}
            onChange={e => writeFanTarget(1, Number(e.target.value))} />
          <span className="pv">{fan2TargetRpm} <small>RPM</small></span>
        </div>
        <div className="fan-manual-actions">
          <button className="btn ghost" onClick={resetFanTargets} disabled={curveActive || !anyCustom}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M3 12a9 9 0 1 0 3-6.7M3 4v5h5"/></svg>
            恢复模式默认
          </button>
          <span className="fm-state">
            {anyCustom
              ? "当前为自定义固定转速，已持久化到当前配置，切换模式/重开应用后仍会保留"
              : "当前为模式默认转速，由固件曲线控制"}
          </span>
        </div>
        <div className="hint">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M12 9v4M12 17h.01"/><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg>
          手动调速与自定义曲线互斥；可调范围随散热模式变化（安静 1900–2900，均衡 2600–3500，野兽 3200–3800）。斗战档由固件接管风扇，手动/曲线写入无效；曲线目标超出风扇物理上限（大扇 ~3000、小扇 ~7200）时自动钳位。
        </div>
      </div>

      {/* 自定义风扇曲线 */}
      <div className="section-title">自定义风扇曲线<span className="line"></span></div>
      <div className="card reveal enter" style={{ animationDelay: ".1s" }}>
        <FanCurvePanel mode={settings.mode} overrides={overrides} onCurveActiveChange={setCurveActive} />
      </div>
    </section>
  );
}
