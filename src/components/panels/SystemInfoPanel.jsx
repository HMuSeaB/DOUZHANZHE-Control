import { useState, useEffect, useCallback, useRef } from "react";
import { Skeleton, OfflineCard, EmptyState } from "../ui/PageState";

const LS_SYS_INFO = "douzhanzhe_sys_info";
const LS_SYS_EXT  = "douzhanzhe_sys_info_ext";
const LS_CACHE_VER = "douzhanzhe_cache_ver";

if (localStorage.getItem(LS_CACHE_VER) !== "3") {
  localStorage.removeItem(LS_SYS_INFO);
  localStorage.removeItem(LS_SYS_EXT);
  localStorage.setItem(LS_CACHE_VER, "3");
}

const ICONS = {
  cpu: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="6" y="6" width="12" height="12" rx="1.5"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/></svg>,
  gpu: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="2" y="6" width="20" height="12" rx="1.5"/><circle cx="9" cy="12" r="3"/><path d="M16 10h3M16 14h3"/></svg>,
  ram: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="8" width="18" height="8" rx="1"/><path d="M7 8V6M12 8V6M17 8V6M7 16v2M12 16v2M17 16v2"/></svg>,
  disk: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="4" width="18" height="6" rx="1.5"/><rect x="3" y="14" width="18" height="6" rx="1.5"/><path d="M7 7h.01M7 17h.01"/></svg>,
  board: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M8 8h8v8H8z"/><path d="M3 9h2M3 15h2M19 9h2M19 15h2M9 3v2M15 3v2M9 19v2M15 19v2"/></svg>,
  battery: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="2" y="7" width="18" height="10" rx="2"/><path d="M22 10v4"/><path d="M6 10v4M10 10v4"/></svg>,
  os: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="3" width="8" height="8" rx="1"/><rect x="13" y="3" width="8" height="8" rx="1"/><rect x="3" y="13" width="8" height="8" rx="1"/><rect x="13" y="13" width="8" height="8" rx="1"/></svg>,
  device: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="4" width="18" height="12" rx="2"/><path d="M2 20h20M8 20v-2M16 20v-2"/></svg>,
};

function formatNumber(n, digits = 2) {
  if (n == null || isNaN(n)) return "—";
  return Number(n).toFixed(digits).replace(/\.0+$/, "");
}

function SysInfoSkeleton() {
  return (
    <div className="spec-grid" aria-hidden="true">
      {Array.from({ length: 8 }).map((_, i) => (
        <div className="card spec-card skeleton-card" key={i}>
          <div className="sc-head">
            <Skeleton className="sk-chip" />
            <span style={{ flex: 1, minWidth: 0 }}>
              <Skeleton className="sk-line" style={{ width: "46%" }} />
              <Skeleton className="sk-line" style={{ width: "68%", marginTop: 7 }} />
            </span>
          </div>
          <div className="sc-body">
            {Array.from({ length: 4 }).map((_, r) => (
              <div className="spec-row" key={r}>
                <Skeleton className="sk-line" style={{ width: "34%" }} />
                <Skeleton className="sk-line" style={{ width: "46%" }} />
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

export default function SystemInfoPanel({ trigger, onRefreshDone }) {
  const onRefreshDoneRef = useRef(onRefreshDone);
  useEffect(() => { onRefreshDoneRef.current = onRefreshDone; });

  const [info, setInfo] = useState(() => {
    try { const r = localStorage.getItem(LS_SYS_INFO); return r ? JSON.parse(r) : null; } catch { return null; }
  });
  const [ext, setExt] = useState(() => {
    try { const r = localStorage.getItem(LS_SYS_EXT); return r ? JSON.parse(r) : null; } catch { return null; }
  });
  const [loading, setLoading] = useState(!info || !ext);
  const [offline, setOffline] = useState(false);
  const [diag, setDiag] = useState(null);
  const [diagLoading, setDiagLoading] = useState(false);

  const fetchDiag = useCallback(async () => {
    setDiagLoading(true);
    try {
      const res = await fetch("/api/diagnostics/health");
      const data = await res.json();
      if (data.ok) setDiag(data);
    } catch { /* offline */ }
    finally { setDiagLoading(false); }
  }, []);

  useEffect(() => {
    fetchDiag();
  }, [fetchDiag]);

  const fetchData = useCallback(async (isUserRefresh = false) => {
    setLoading(true);
    try {
      const [r1, r2] = await Promise.all([
        fetch("/api/system/info").then(r => r.json()).catch(() => null),
        fetch("/api/system/info-ext").then(r => r.json()).catch(() => null),
      ]);
      if (r1) { setInfo(r1); localStorage.setItem(LS_SYS_INFO, JSON.stringify(r1)); }
      if (r2) { setExt(r2); localStorage.setItem(LS_SYS_EXT, JSON.stringify(r2)); }
      setOffline(!r1 && !r2);
    } finally {
      setLoading(false);
      if (isUserRefresh) {
        onRefreshDoneRef.current?.();
      }
    }
  }, []);

  // 首次挂载：仅在本地缓存不完整时拉取数据；已有缓存则直接秒开，避免切换 Tab 时反复调起后端 PowerShell
  useEffect(() => {
    if (!info || !ext) {
      fetchData(false);
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // 用户点击外部“刷新”按钮时强制刷新
  const lastTriggerRef = useRef(trigger);
  useEffect(() => {
    if (trigger > 0 && trigger !== lastTriggerRef.current) {
      lastTriggerRef.current = trigger;
      fetchData(true);
    }
  }, [trigger, fetchData]);

  const i = info || {};
  const e = ext || {};
  const hasCache = !!(info || ext);

  const battHealth = e.battDesign > 0 ? (e.battFull / e.battDesign * 100) : 0;

  // 实跑低于额定说明 BIOS 没跑满(如 5200/5600),解锁 BIOS 改频后两值一致
  const memSpeed = e.memorySpeed;
  const memRated = e.memorySpeedRated;
  const memSpeedText = memSpeed
    ? (memRated && memRated !== memSpeed ? `${memSpeed} MT/s · 额定 ${memRated}` : `${memSpeed} MT/s`)
    : "—";

  const cards = [
    {
      id: "cpu",
      title: "处理器 CPU",
      subtitle: i.cpuName || "—",
      icon: ICONS.cpu,
      rows: [
        { k: "核心 / 线程", v: i.cpuCores ? `${i.cpuCores} 核 ${i.cpuThreads || "?"} 线程` : "—" },
        { k: "基础频率", v: i.cpuBaseFreq ? `${formatNumber(i.cpuBaseFreq)} GHz` : "—" },
        { k: "最大睿频", v: i.cpuMaxFreq ? `${formatNumber(i.cpuMaxFreq)} GHz` : "—" },
        { k: "三级缓存", v: i.cpuL3Cache ? `${i.cpuL3Cache} MB` : "—" },
      ],
    },
    {
      id: "gpu",
      title: "显卡 GPU",
      subtitle: i.gpuDiscrete || "—",
      icon: ICONS.gpu,
      rows: [
        { k: "显存容量", v: i.gpuVramGB ? `${i.gpuVramGB} GB` : "—" },
        { k: "功耗上限 TGP", v: i.gpuTgp ? `${i.gpuTgp} W` : "—" },
        { k: "驱动版本", v: e.nvDriver || "—" },
        { k: "DirectX", v: i.gpuDx || "—" },
      ],
    },
    {
      id: "memory",
      title: "内存",
      subtitle: e.memoryType || "—",
      icon: ICONS.ram,
      rows: [
        { k: "总容量", v: i.memoryTotalGB ? `${i.memoryTotalGB} GB` : "—" },
        { k: "频率", v: memSpeedText },
        { k: "插槽占用", v: e.sticks ? `${e.sticks.length} / ${e.memorySlots || e.sticks.length}` : "—" },
        { k: "时序", v: e.memoryTiming || "—" },
      ],
    },
    {
      id: "storage",
      title: "存储",
      subtitle: e.diskModel || "—",
      icon: ICONS.disk,
      rows: [
        { k: "总容量", v: i.diskTotalGB ? `${(i.diskTotalGB / 1024).toFixed(2)} TB` : "—" },
        { k: "剩余空间", v: i.diskFreeGB ? `${(i.diskFreeGB / 1024).toFixed(2)} TB` : "—" },
        { k: "接口", v: e.diskInterface || "—" },
        { k: "健康度", v: e.diskHealth ? `${e.diskHealth} %` : "—" },
      ],
    },
    {
      id: "board",
      title: "主板 / BIOS",
      subtitle: e.boardInfo || i.systemModel || "—",
      icon: ICONS.board,
      rows: [
        { k: "BIOS 版本", v: e.biosVersion || "—" },
        { k: "EC 固件", v: e.ecVersion || "—" },
        { k: "出厂日期", v: e.biosDate || "—" },
        { k: "序列号", v: e.serialNumber || "—" },
      ],
    },
    {
      id: "battery",
      title: "电池",
      subtitle: e.battChemistry || "锂离子电池组",
      icon: ICONS.battery,
      rows: [
        { k: "设计容量", v: e.battDesign > 0 ? `${(e.battDesign / 1000).toFixed(2)} Wh` : "—" },
        { k: "当前最大容量", v: e.battFull > 0 ? `${(e.battFull / 1000).toFixed(2)} Wh` : "—" },
        { k: "bar", v: battHealth },
        { k: "note", v: `健康度 ${battHealth.toFixed(1)}% · 已循环 ${e.battCycle || 0} 次 · ${e.battStatus || "状态良好"}` },
      ],
    },
    {
      id: "os",
      title: "操作系统",
      subtitle: e.osName || "—",
      icon: ICONS.os,
      rows: [
        { k: "版本", v: e.osVersion || "—" },
        { k: "内部版本号", v: e.osBuild || "—" },
        { k: "系统类型", v: e.osArch || "64 位" },
        { k: "安装日期", v: e.osInstallDate || "—" },
      ],
    },
    {
      id: "device",
      title: "设备概览",
      subtitle: i.systemManufacturer || "DOUZHANZHE 斗战者",
      icon: ICONS.device,
      rows: [
        { k: "机型", v: i.systemModel || "—" },
        { k: "显示屏", v: e.displaySpec || "—" },
        { k: "网卡", v: e.networkCard || "—" },
        { k: "保修状态", v: e.warrantyStatus ? `${e.warrantyStatus}` : "—" },
      ],
    },
  ];

  if (loading && !hasCache) {
    return <SysInfoSkeleton />;
  }

  if (offline && !hasCache) {
    return (
      <OfflineCard
        title="系统信息暂不可用"
        description="无法连接后端服务，当前没有可用的本地缓存。"
        onRetry={fetchData}
        retrying={loading}
      />
    );
  }

  if (!loading && !offline && !hasCache) {
    return (
      <EmptyState
        title="暂无系统信息"
        description="后端未返回硬件信息，请稍后重试。"
        action={<button className="btn" onClick={fetchData}>重试</button>}
      />
    );
  }

  return (
    <>
      {offline && (
        <OfflineCard
          title="系统信息来自本地缓存"
          description="后端服务未连接，以下为上次加载的硬件信息。"
          onRetry={fetchData}
          retrying={loading}
        />
      )}

      {/* 硬件与驱动通道健康自检 */}
      <div className="card reveal enter" style={{ padding: "14px 20px", marginBottom: "16px" }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: "12px" }}>
          <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
            <span style={{ fontSize: "16px" }}>🩺</span>
            <b style={{ fontSize: "14px" }}>底层驱动与硬件通道健康诊断</b>
            {diag && (
              <span style={{
                fontSize: "11px",
                padding: "2px 8px",
                borderRadius: "4px",
                background: diag.healthy ? "rgba(126, 231, 135, 0.15)" : "rgba(248, 81, 73, 0.15)",
                color: diag.healthy ? "#7ee787" : "#f85149",
                border: `1px solid ${diag.healthy ? "rgba(126, 231, 135, 0.3)" : "rgba(248, 81, 73, 0.3)"}`,
              }}>
                {diag.healthy ? "通道全绿 · 健康" : "存在异常通道"}
              </span>
            )}
          </div>
          <button className="btn ghost" onClick={fetchDiag} disabled={diagLoading} style={{ padding: "4px 10px", fontSize: "12px" }}>
            {diagLoading ? "诊断中..." : "重新自检"}
          </button>
        </div>

        {diag?.items ? (
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "10px" }}>
            {diag.items.map((item) => {
              const isPass = item.status === "pass";
              const isWarn = item.status === "warn";
              const color = isPass ? "#7ee787" : isWarn ? "#e3b341" : "#f85149";
              return (
                <div key={item.key} style={{
                  background: "rgba(255, 255, 255, 0.02)",
                  border: "1px solid rgba(255, 255, 255, 0.06)",
                  borderRadius: "6px",
                  padding: "10px 12px",
                }}>
                  <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: "4px" }}>
                    <span style={{ fontSize: "13px", fontWeight: 600 }}>{item.name}</span>
                    <span style={{
                      display: "inline-block",
                      width: "8px",
                      height: "8px",
                      borderRadius: "50%",
                      background: color,
                      boxShadow: `0 0 6px ${color}`,
                    }} />
                  </div>
                  <div style={{ fontSize: "11px", color: "#8b949e", marginBottom: "2px" }}>
                    {item.detail}
                  </div>
                  {item.latencyMs > 0 && (
                    <div style={{ fontSize: "10px", color: "#6e7681" }}>
                      往返时延: {item.latencyMs} ms
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        ) : (
          <div style={{ fontSize: "12px", color: "#8b949e" }}>正在探查硬件总线通道状态...</div>
        )}
      </div>

      <div className="spec-grid">
        {cards.map((card, idx) => (
          <div key={card.id} className="card spec-card reveal enter" style={{ animationDelay: `${0.02 + idx * 0.03}s` }}>
            <div className="sc-head">
              <span className="ic">{card.icon}</span>
              <span><b>{card.title}</b><small>{card.subtitle}</small></span>
            </div>
            <div className="sc-body">
              {card.rows.map((row, ridx) => {
                if (row.k === "bar") {
                  return (
                    <div key={ridx}>
                      <div className="batt-bar"><i style={{ width: `${Math.min(100, Math.max(0, row.v))}%` }}></i></div>
                    </div>
                  );
                }
                if (row.k === "note") {
                  return <div key={ridx} className="batt-note">{row.v}</div>;
                }
                return (
                  <div key={ridx} className="spec-row">
                    <span className="k">{row.k}</span>
                    <span className="v">{row.v}</span>
                  </div>
                );
              })}
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
