import { setProfileThermalMode } from '../../services/uxtuAdapter';

const THERMAL_MODES = [
  { id: 'silent', label: '安静' },
  { id: 'office', label: '均衡' },
  { id: 'beast',  label: '野兽' },
  { id: 'gaming', label: '斗战' },
];

export default function ThermalModeRow({ currentProfile, platformInfo, setCurrentProfile }) {
  if (!currentProfile || currentProfile.builtIn) return null;
  if (platformInfo.oem !== 'Bellator') return null;

  const currentMode = currentProfile.thermalMode || 'office';

  async function handleSwitch(mode) {
    if (mode === currentMode) return;
    try {
      await setProfileThermalMode(currentProfile.id, mode);
      setCurrentProfile(prev => prev ? { ...prev, thermalMode: mode } : prev);
    } catch (e) { console.error('thermal mode switch failed:', e); }
  }

  return (
    <>
      <div className="section-title">
        {'\u914d\u7f6e\u53c2\u6570'} <span className="tag">{'\u4ec5\u7528\u6237\u914d\u7f6e\u53ef\u89c1'}</span>
        <span className="line" />
      </div>
      <div className="card reveal enter" style={{ padding: '6px 20px 14px', animationDelay: '.06s' }}>
        <div className="mode-line">
          <span className="lab">
            <b>{'\u6563\u70ed\u6a21\u5f0f'}</b>
            <small>{'\u9009\u62e9\u8be5\u914d\u7f6e\u89e6\u53d1\u5207\u6362\u7684 BIOS \u6a21\u5f0f \u00b7 \u5185\u7f6e\u914d\u7f6e\u56fa\u5b9a\u4e0d\u53ef\u6539'}</small>
          </span>
          <div className="segmented" style={{ marginLeft: 'auto' }}>
            {THERMAL_MODES.map(m => (
              <button
                key={m.id}
                className={currentMode === m.id ? 'active' : ''}
                onClick={() => handleSwitch(m.id)}
              >
                {m.label}
              </button>
            ))}
          </div>
        </div>
      </div>
    </>
  );
}
