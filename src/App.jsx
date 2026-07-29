import { useState, useEffect } from "react";
import { useControlState } from "./hooks/useControlState";
import Dashboard from "./pages/Dashboard";

const PAGES = [
  { key: "dashboard", label: "仪表盘", icon: "M3 12l9-8 9 8M5 10v10h14V10" },
  { key: "control",  label: "控制面板", icon: "M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0" },
  { key: "fan",      label: "风扇控制", icon: "M12 3a4 4 0 0 1 4 4c0 1.5-.8 2.7-2 3.4V13h3a3 3 0 0 1 3 3v1M8 7M6 20v-2a3 3 0 0 1 3-3h1" },
  { key: "platform", label: "平台控制", icon: "M3 4h18v14H3M7 21h10M9 8h6M9 12h4" },
  { key: "games",    label: "游戏", icon: "M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2M9 9l3 3-3 3M14 15h3" },
  { key: "sysinfo",  label: "系统信息", icon: "M4 4h16v16H4M9 4v16M4 9h5M13 8h4M13 12h4" },
  { key: "settings", label: "设置", icon: "M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2" },
];

export default function App() {
  const { theme, backendOnline } = useControlState();

  const [activePage, setActivePage] = useState(() => {
    try { return localStorage.getItem("dz_page") || "dashboard"; } catch { return "dashboard"; }
  });
  useEffect(() => {
    try { localStorage.setItem("dz_page", activePage); } catch {}
  }, [activePage]);

  useEffect(() => { document.documentElement.setAttribute("data-theme", theme); }, [theme]);

  function Svg({ d }) {
    return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d={d}/></svg>;
  }

  return (
    <div className="app">
      {/* 全局工具栏 */}
      <header className="toolbar">
        <span className="env-pill"><span className="dot" data-online={backendOnline}></span>{backendOnline ? "后端已连接" : "后端离线"}</span>
        <div className="spacer"></div>
        <button className="theme-toggle" onClick={() => setTheme(theme === "dark" ? "light" : "dark")} title="切换深浅主题">
          <svg className="moon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z"/></svg>
          <svg className="sun" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><circle cx="12" cy="12" r="4.2"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>
        </button>
      </header>

      {/* 窄边栏 */}
      <nav className="sidebar">
        {PAGES.map(p => (
          <button key={p.key} className={`nav-item${activePage === p.key ? ' active' : ''}`}
            onClick={() => setActivePage(p.key)}>
            <Svg d={p.icon} />
            <span>{p.label}</span>
          </button>
        ))}
        <div className="grow"></div>
      </nav>

      {/* 内容区 */}
      <main className="content">
        {activePage === "dashboard" && <Dashboard />}
        {activePage === "control" && <ControlPanelPage />}
        {activePage === "fan" && <FanControlPage />}
        {activePage === "platform" && <PlatformControlPage />}
        {activePage === "games" && <GamesPage />}
        {activePage === "sysinfo" && <SysInfoPage />}
        {activePage === "settings" && <SettingsPage />}
      </main>
    </div>
  );
}

/* ======== 页面组件（逐步从原型迁移） ======== */
function ControlPanelPage() {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>控制面板</h1>
          <p>CPU / GPU 参数调节</p>
        </div>
      </div>
      <p style={{color:'var(--fg-3)',padding:'20px 0'}}>控制面板 — 待从原型迁移</p>
    </section>
  );
}
function FanControlPage() {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>风扇控制</h1>
          <p>EC 寄存器绑定 · 手动调速与自定义曲线</p>
        </div>
      </div>
      <p style={{color:'var(--fg-3)',padding:'20px 0'}}>风扇控制 — 待从原型迁移</p>
    </section>
  );
}
function PlatformControlPage() {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>平台控制</h1>
          <p>EC / WMI 型号绑定控制</p>
        </div>
      </div>
      <p style={{color:'var(--fg-3)',padding:'20px 0'}}>平台控制 — 待从原型迁移</p>
    </section>
  );
}
function GamesPage() {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>游戏</h1>
          <p>按游戏自动切换参数预设</p>
        </div>
      </div>
      <p style={{color:'var(--fg-3)',padding:'20px 0'}}>游戏 — 待从原型迁移</p>
    </section>
  );
}
function SysInfoPage() {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>系统信息</h1>
          <p>硬件配置详情</p>
        </div>
      </div>
      <p style={{color:'var(--fg-3)',padding:'20px 0'}}>系统信息 — 待从原型迁移</p>
    </section>
  );
}
function SettingsPage() {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>设置</h1>
          <p>应用级配置</p>
        </div>
      </div>
      <p style={{color:'var(--fg-3)',padding:'20px 0'}}>设置 — 待从原型迁移</p>
    </section>
  );
}
