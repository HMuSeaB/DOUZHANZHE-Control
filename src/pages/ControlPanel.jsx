import { useState, useEffect } from 'react';
import { useControlState } from '../hooks/useControlState';
import { resetProfile, resetToFactoryDefaults } from '../services/uxtuAdapter';
import ConfigBar from '../components/panels/ConfigBar';
import ThermalModeRow from '../components/panels/ThermalModeRow';
import ProfileManageModal from '../components/panels/ProfileManageModal';
import PerformancePanel from '../components/panels/PerformancePanel';
import IntelCpuPanel from '../components/panels/IntelCpuPanel';
import IntelPowerPanel from '../components/panels/IntelPowerPanel';
import AmdCpuPanel from '../components/panels/AmdCpuPanel';
import AmdPowerPanel from '../components/panels/AmdPowerPanel';

const ACC_STATE_KEY = 'dz_control_acc';
const DEFAULT_ACC_STATE = {
  cpuFreqOpen: true,
  cpuPowerOpen: false,
  gpuOcOpen: true,
};

function loadAccState() {
  try {
    const raw = localStorage.getItem(ACC_STATE_KEY);
    if (!raw) return { ...DEFAULT_ACC_STATE };
    const parsed = JSON.parse(raw);
    return {
      cpuFreqOpen: typeof parsed.cpuFreqOpen === 'boolean' ? parsed.cpuFreqOpen : DEFAULT_ACC_STATE.cpuFreqOpen,
      cpuPowerOpen: typeof parsed.cpuPowerOpen === 'boolean' ? parsed.cpuPowerOpen : DEFAULT_ACC_STATE.cpuPowerOpen,
      gpuOcOpen: typeof parsed.gpuOcOpen === 'boolean' ? parsed.gpuOcOpen : DEFAULT_ACC_STATE.gpuOcOpen,
    };
  } catch {
    return { ...DEFAULT_ACC_STATE };
  }
}

export default function ControlPanel() {
  const {
    telemetry, settings, setSettings, uxtuParams, setUxtuParams,
    overrides, saveOverride, clearOverride, switching,
    profiles, setProfiles, currentProfile, setCurrentProfile,
    platformInfo, switchProfile, afterProfileDeleted, afterProfileCreated, refreshOverrides,
  } = useControlState();

  const [cpuVendor, setCpuVendor] = useState(null);
  const gpuMode = telemetry?.gpuMode ?? null;
  const [manageOpen, setManageOpen] = useState(false);
  const [accState, setAccState] = useState(loadAccState);
  const { cpuFreqOpen, cpuPowerOpen, gpuOcOpen } = accState;

  useEffect(() => {
    fetch('/api/platform/info')
      .then(r => r.json())
      .then(d => setCpuVendor(d.vendor))
      .catch(() => setCpuVendor('unknown'));
  }, []);

  useEffect(() => {
    try { localStorage.setItem(ACC_STATE_KEY, JSON.stringify(accState)); } catch {}
  }, [accState]);

  function toggleAcc(key) {
    setAccState(prev => ({ ...prev, [key]: !prev[key] }));
  }

  const handleProfileReset = async (id) => {
    try {
      await resetProfile(id);
      if (currentProfile?.id === id || settings.mode === id) {
        await resetToFactoryDefaults(settings.mode);
        await refreshOverrides();
      }
    } catch (e) { console.error('profile reset failed:', e); }
  };

  const panelProps = { settings, setSettings, uxtuParams, setUxtuParams, overrides, saveOverride, clearOverride, switching };

  return (
    <section className="page active">
      <div className="page-head">
        <div>
          <h1>{'\u63a7\u5236\u9762\u677f'}</h1>
          <p>{'CPU / GPU \u53c2\u6570\u8c03\u8282 \u00b7 \u9884\u8bbe\u7ba1\u7406\u4e2d\u5fc3 \u00b7 \u8c03\u8282\u5b9e\u65f6\u4fdd\u5b58\u5230\u5f53\u524d\u914d\u7f6e'}</p>
        </div>
      </div>

      {/* Profile management bar */}
      <ConfigBar
        profiles={profiles}
        currentProfile={currentProfile}
        switching={switching}
        settings={settings}
        switchProfile={switchProfile}
        afterProfileCreated={afterProfileCreated}
        onManageOpen={() => setManageOpen(true)}
      />

      {/* Thermal mode row (user profiles only, Bellator only) */}
      <ThermalModeRow
        currentProfile={currentProfile}
        platformInfo={platformInfo}
        setCurrentProfile={setCurrentProfile}
      />

      {/* CPU accordion */}
      <div className="section-title">CPU<span className="line" /></div>
      <div className="card accordion reveal enter" style={{ animationDelay: '.1s' }}>
        <div className={'acc-item' + (cpuFreqOpen ? ' open' : '')}>
          <button className="acc-head" onClick={() => toggleAcc('cpuFreqOpen')}>
            <span className="ic">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="6" y="6" width="12" height="12" rx="1.5"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/></svg>
            </span>
            <span className="ht">
              <b>{'CPU \u9891\u7387\u63a7\u5236'}</b>
              <small>{'\u9891\u7387\u4e0a\u9650 \u00b7 \u7535\u6e90\u7ba1\u7406 \u00b7 \u710e\u901f'}</small>
            </span>
            <span className="chev"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9"><path d="m6 9 6 6 6-6"/></svg></span>
          </button>
          <div className="acc-body"><div className="acc-inner">
            {cpuVendor === 'AMD'
              ? <AmdCpuPanel {...panelProps} />
              : cpuVendor === null ? null
              : <IntelCpuPanel {...panelProps} />}
          </div></div>
        </div>
        <div className={'acc-item' + (cpuPowerOpen ? ' open' : '')}>
          <button className="acc-head" onClick={() => toggleAcc('cpuPowerOpen')}>
            <span className="ic">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M13 2 3 14h7l-1 8 10-12h-7l1-8Z"/></svg>
            </span>
            <span className="ht">
              <b>{'CPU \u529f\u8017\u4e0e\u6e29\u5ea6'}</b>
              <small>{'\u6e29\u5ea6\u5899 \u00b7 \u964d\u538b \u00b7 PPT/PL2'}</small>
            </span>
            <span className="chev"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9"><path d="m6 9 6 6 6-6"/></svg></span>
          </button>
          <div className="acc-body"><div className="acc-inner">
            {cpuVendor === 'AMD'
              ? <AmdPowerPanel {...panelProps} />
              : cpuVendor === null ? null
              : <IntelPowerPanel {...panelProps} />}
          </div></div>
        </div>
      </div>

      {/* GPU accordion */}
      <div className="section-title">GPU<span className="line" /></div>
      <div className="card accordion reveal enter" style={{ animationDelay: '.14s' }}>
        <div className={'acc-item' + (gpuOcOpen ? ' open' : '')}>
          <button className="acc-head" onClick={() => toggleAcc('gpuOcOpen')}>
            <span className="ic">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="2" y="6" width="20" height="12" rx="1.5"/><circle cx="9" cy="12" r="3"/><path d="M16 10h3M16 14h3"/></svg>
            </span>
            <span className="ht">
              <b>{'GPU \u9891\u7387\u4e0e\u8d85\u9891'}</b>
              <small>{'\u6838\u5fc3 / \u663e\u5b58\u9891\u7387\u504f\u79fb'}</small>
            </span>
            <span className="chev"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9"><path d="m6 9 6 6 6-6"/></svg></span>
          </button>
          <div className="acc-body"><div className="acc-inner">
            <PerformancePanel {...panelProps} showCpu={false} showPower={false} showGpu={true} gpuMode={gpuMode} />
          </div></div>
        </div>
      </div>

      {/* Profile manage modal */}
      <ProfileManageModal
        open={manageOpen}
        onClose={() => setManageOpen(false)}
        profiles={profiles}
        setProfiles={setProfiles}
        currentProfile={currentProfile}
        setCurrentProfile={setCurrentProfile}
        switchProfile={switchProfile}
        afterProfileDeleted={afterProfileDeleted}
        onResetProfile={handleProfileReset}
      />
    </section>
  );
}
