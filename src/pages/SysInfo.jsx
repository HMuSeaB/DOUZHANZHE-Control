import SystemInfoPanel from "../components/panels/SystemInfoPanel";

export default function SysInfo() {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>系统信息</h1>
          <p>硬件配置详情</p>
        </div>
      </div>

      <div className="reveal enter" style={{ maxWidth: 700 }}>
        <SystemInfoPanel />
      </div>
    </section>
  );
}
