import SettingsPanel from "../components/panels/SettingsPanel";

export default function Settings({ theme, setTheme }) {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>设置</h1>
          <p>应用级配置 · 非硬件控制 · 更改即时生效并本地持久化</p>
        </div>
      </div>

      <div className="reveal enter">
        <SettingsPanel theme={theme} setTheme={setTheme} />
      </div>
    </section>
  );
}
