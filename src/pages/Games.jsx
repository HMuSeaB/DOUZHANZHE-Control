import GameProfilesPanel from "../components/panels/GameProfilesPanel";

export default function Games() {
  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>游戏</h1>
          <p>按游戏自动切换参数预设 · 扫描并添加</p>
        </div>
      </div>

      <div className="reveal enter" style={{ animationDelay: ".04s" }}>
        <GameProfilesPanel />
      </div>
    </section>
  );
}
