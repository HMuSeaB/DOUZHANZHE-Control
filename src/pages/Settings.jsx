import { useControlState } from "../hooks/useControlState";
import SettingsPanel from "../components/panels/SettingsPanel";

export default function Settings() {
  const { settings, setSettings } = useControlState();

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>设置</h1>
          <p>应用级配置 · 快捷键 · 主题 · 关于</p>
        </div>
      </div>

      <div style={{ maxWidth: 600 }} className="reveal enter">
        <SettingsPanel
          settings={settings}
          setSettings={setSettings}
          showSwitches={true}
          showKeyboard={true}
          showAbout={true}
          showAutoStart={true}
          showBackground={true}
          showHotkey={true}
        />
      </div>
    </section>
  );
}
