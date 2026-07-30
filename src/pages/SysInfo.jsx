import { useState } from "react";
import SystemInfoPanel from "../components/panels/SystemInfoPanel";

const refreshIcon = <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9"><path d="M21 12a9 9 0 1 1-2.6-6.4"/><path d="M21 3v6h-6"/></svg>;

export default function SysInfo() {
  const [refreshing, setRefreshing] = useState(false);
  const [trigger, setTrigger] = useState(0);

  const handleRefresh = () => {
    if (refreshing) return;
    setRefreshing(true);
    setTrigger(t => t + 1);
  };

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>系统信息</h1>
          <p>硬件配置详情 · 数据来源 WMI / DMI · 首次加载后本地缓存</p>
        </div>
        <button className={`btn ${refreshing ? "spin" : ""}`} onClick={handleRefresh} disabled={refreshing}>
          {refreshIcon}
          {refreshing ? "刷新中..." : "刷新"}
        </button>
      </div>

      <div className="reveal enter">
        <SystemInfoPanel trigger={trigger} onRefreshDone={() => setRefreshing(false)} />
      </div>
    </section>
  );
}
