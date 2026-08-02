import { useCallback, useEffect, useRef, useState } from "react";
import {
  createTelemetrySocket, FULL_PARAMS, MODE_FAN_DEFAULTS,
  fetchOverrides, switchMode, syncOverrides, log,
  migrateLocalStorageOverrides, flattenBackendOverrides,
  fetchTelemetry, fetchUiState, saveUiState,
  fetchProfiles, fetchPlatformInfo,
  clearOverrides,
  setMaxCoresForPercent,
} from "../services/uxtuAdapter";

let _maxCores = 16; // 模块级缓存，供模式切换使用

export function useControlState() {

  // ── Theme ──
  const [theme, setTheme] = useState(() => document.documentElement?.getAttribute("data-theme-mode") || "dark");

  // ── Telemetry + History ──
  const [telemetry, setTelemetry] = useState({});
  const lastTickRef = useRef(null);
  const MAX_HISTORY = 60;
  const [history, setHistory] = useState({ cpu: [], gpu: [], fan: [], cpuTemp: [], gpuTemp: [] });
  const prevTelemetryRef = useRef(telemetry);
  useEffect(() => {
    if (prevTelemetryRef.current === telemetry) return;
    prevTelemetryRef.current = telemetry;
    setHistory((prev) => ({
      cpu: [...prev.cpu, telemetry.cpuUsage].slice(-MAX_HISTORY),
      gpu: [...prev.gpu, telemetry.gpuUsage].slice(-MAX_HISTORY),
      fan: [...prev.fan, telemetry.fanLargeRpm].slice(-MAX_HISTORY),
      cpuTemp: [...prev.cpuTemp, telemetry.cpuTemp].slice(-MAX_HISTORY),
      gpuTemp: [...prev.gpuTemp, telemetry.gpuTemp].slice(-MAX_HISTORY),
    }));
  }, [telemetry]);

  // ── Settings (含 mode) ──
  // 配置以 /api/overrides 与 /api/ui-state 为唯一权威源，不在 localStorage 持久化
  const [settings, setSettings] = useState({
    mode: "office", dGpuDirect: true, fanBoost: false,
    numLock: true, capsLock: false, fnLock: false,
    touchpadLock: false, osdDisabled: false, kbBrightnessLevel: 0,
  });

  // ── uxtuParams: 唯一全量参数状态 (FULL_PARAMS 兆底 + overrides 覆盖) ──
  const [uxtuParams, setUxtuParams] = useState(() => {
    const fanDefaults = MODE_FAN_DEFAULTS["office"] || {};
    return { ...FULL_PARAMS, ...fanDefaults };
  });

  // ── Overrides 状态（暴露给组件，用于灰色/高亮显示）──
  const [overrides, setOverrides] = useState({});

  // 标记首次加载，防止 fetchOverrides 设置 mode 时触发 switchMode 副作用
  const initialLoadRef = useRef(true);

  // 启动时：先迁移 localStorage 旧数据到后端，再拉取 overrides
  useEffect(() => {
    (async () => {
      try {
        const count = await migrateLocalStorageOverrides();
        if (count > 0) log("Startup", `migrated ${count} mode(s) from localStorage`);
      } catch (e) {
        log("Startup", `localStorage migration error: ${e.message}`);
      }
      // 平台检测：确定核心数转换基数
      let maxCores = 16;
      try {
        const plat = await (await fetch("/api/platform/info")).json();
        if (plat?.vendor === "Intel") maxCores = 18;
        setMaxCoresForPercent(maxCores);
      } catch { /* 后端不可用时默认 16 */ }
      _maxCores = maxCores;
      // 加载机型信息
      try {
        const pi = await fetchPlatformInfo();
        setPlatformInfo({
          oem: pi.oem || 'Unknown',
          vendor: pi.vendor || '',
          model: pi.model || '',
          isElevated: pi.isElevated ?? null,
          ecCpuTemp: pi.ecCpuTemp ?? null,
          ecGpuTemp: pi.ecGpuTemp ?? null,
        });
        setPlatformInfoReady(true);
      } catch { /* ignore */ }
      // 加载配置列表
      let profileList = [];
      try {
        const { profiles: list } = await fetchProfiles();
        profileList = list || [];
        console.log('[useControlState] profiles loaded:', profileList.length, profileList.map(p => p.id));
        setProfiles(profileList);
      } catch (e) { console.error('[useControlState] profiles fetch error:', e); }
      // 先 HTTP 拿初始遥测数据，避免 mock 闪烁
      try {
        const initialTel = await fetchTelemetry();
        if (initialTel) { setTelemetry(initialTel); setBackendOnline(true); }
      } catch { /* 后端离线时保持 null，等 WebSocket 或 mock 托底 */ }
      try {
        const { mode, overrides: rawOv } = await fetchOverrides();
        const ov = flattenBackendOverrides(rawOv, maxCores);
        setSettings(prev => {
          prevModeRef.current = mode; // 同步 prevModeRef，防止触发 switchMode
          return { ...prev, mode };
        });
        const fanDefaults = MODE_FAN_DEFAULTS[mode] || {};
        setUxtuParams({ ...FULL_PARAMS, ...fanDefaults, ...ov });
        setOverrides(ov);
        // Sync currentProfile from profiles list
        const curProf = profileList.find(p => p.id === mode) || null;
        setCurrentProfile(curProf);
        initialLoadRef.current = false;
      } catch {
        // 后端不可用时回退到 FULL_PARAMS 默认
        initialLoadRef.current = false;
      }
    })();
  }, []);

  // ── 重置参数到官方默认 ──
  const resetParams = useCallback(async (mode) => {
    await syncOverrides(mode, {});
    setOverrides({});
    const fanDefaults = MODE_FAN_DEFAULTS[mode] || {};
    setUxtuParams({ ...FULL_PARAMS, ...fanDefaults });
  }, []);

  // 从后端加载 UI 状态
  useEffect(() => { (async () => { try { var st = await fetchUiState(); if (st.theme) setTheme(st.theme); if (st.accentColor) document.documentElement.style.setProperty("--seed-primary", st.accentColor); } catch { /* 后端离线时保留默认 */ } })(); }, []);

  // theme 同步到后端
  useEffect(() => { (async () => { try { await saveUiState({ theme }); } catch { /* 后端离线时下次再同步 */ } })(); }, [theme]);
  // ── 模式切换: 立即发送后端请求，切换期间禁用 UI 防止竞争写入 ──
  const prevModeRef = useRef(settings.mode);
  const switchGenRef = useRef(0);
  const [switching, setSwitching] = useState(false);
  const switchTimeoutRef = useRef(null);

  useEffect(() => {
    // 首次加载由 startup useEffect 处理，这里跳过
    if (initialLoadRef.current) return;

    const prevMode = prevModeRef.current;
    const currentMode = settings.mode;
    prevModeRef.current = currentMode;
    if (prevMode === currentMode) return;

    const gen = ++switchGenRef.current;
    setSwitching(true);

    // 安全兜底: 5 秒内后端未响应则强制解锁 UI
    if (switchTimeoutRef.current) clearTimeout(switchTimeoutRef.current);
    switchTimeoutRef.current = setTimeout(() => {
      setSwitching(false);
    }, 5000);

    // 后端 /api/overrides/switch 内部已完成:
    // thermal_mode + last-mode.json + GPU/NVAPI/CPU 重置 + RestoreAllPerfSettings
    switchMode(currentMode).then(({ overrides: rawOv }) => {
      if (gen !== switchGenRef.current) return; // 丢弃过期响应
      const ov = flattenBackendOverrides(rawOv, _maxCores);
      const fanDefaults = MODE_FAN_DEFAULTS[currentMode] || {};
      setUxtuParams({ ...FULL_PARAMS, ...fanDefaults, ...ov });
      setOverrides(ov);
    }).catch(() => {
      if (gen !== switchGenRef.current) return;
      const fanDefaults = MODE_FAN_DEFAULTS[currentMode] || {};
      setUxtuParams({ ...FULL_PARAMS, ...fanDefaults });
      setOverrides({});
    }).finally(() => {
      if (gen === switchGenRef.current) {
        clearTimeout(switchTimeoutRef.current);
        setSwitching(false);
      }
    });
  }, [settings.mode]);

  // ── WebSocket 遥测 + 自动切换 ──
  const [backendOnline, setBackendOnline] = useState(false);
  const [profiles, setProfiles] = useState([]);
  const [currentProfile, setCurrentProfile] = useState(null);
  const [platformInfo, setPlatformInfo] = useState({ oem: 'Unknown', vendor: '', model: '', isElevated: null });
  const [platformInfoReady, setPlatformInfoReady] = useState(false);
  useEffect(() => {
    let disposed = false;
    let ws;
    let reconnectTimer;

    const connect = () => {
      ws = createTelemetrySocket(
        (data) => {
          setBackendOnline(true);

          // 处理自动切换消息
          if (data.type === "auto_switch" && data.mode) {
            log("AutoSwitch", `收到自动切换请求: ${data.mode}`);
            setSettings(prev => prev.mode === data.mode ? prev : { ...prev, mode: data.mode });
            return; // auto_switch 消息不包含遥测数据
          }

          // 处理遥测数据
          setTelemetry(prev => ({ ...prev, ...data }));
          // 自动同步 dGpuDirect 与实际 GPU mode（mode 1=独显→true，0/2→false）
          if (data.gpuMode != null) {
            const gpuMode = parseInt(data.gpuMode);
            const shouldBeOn = gpuMode === 1;
            setSettings(prev => prev.dGpuDirect === shouldBeOn ? prev : { ...prev, dGpuDirect: shouldBeOn });
          }
        },
        () => setBackendOnline(false)
      );
      ws.onclose = () => {
        setBackendOnline(false);
        if (!disposed) reconnectTimer = setTimeout(connect, 3000);
      };
    };

    connect();
    return () => {
      disposed = true;
      clearTimeout(reconnectTimer);
      if (ws) ws.close();
    };
  }, []);

  // ── Mock 模拟 (后端不可用时) ──
  useEffect(() => {
    if (backendOnline) return;
    if (lastTickRef.current === null) lastTickRef.current = Date.now();

    const timer = setInterval(() => {
      const now = Date.now();
      const dt = Math.max(0.5, Math.min(3, (now - (lastTickRef.current || Date.now())) / 1000));
      lastTickRef.current = now;

      setTelemetry(prev => {
        // 首次进入 mock 时用默认值兜底，避免 undefined 参与运算产生 NaN
        const base = {
          cpuUsage: 20, cpuFreq: 2.4, cpuTemp: 45, cpuCores: 16,
          gpuUsage: 15, gpuFreq: 1.2, gpuTemp: 45, gpuVramUsed: 2,
          gpuPowerDrawW: 20, gpuMode: null,
          memoryUsage: 30, memoryTotalGB: 32, memoryFreq: 3200,
          diskUsage: 40, diskTotalGB: 952, diskFreeGB: 400,
          fanLargeRpm: 2200, fanSmallRpm: 2000,
          fanLargeMax: 4400, fanSmallMax: 8200,
          thermalMode: 0, powerPlan: 0, kbBrightness: 0,
          ...prev,
        };
        const fanLargeRpm = Math.round(base.fanLargeRpm + ((uxtuParams.fanLargeRpmTarget ?? 2900) - base.fanLargeRpm) * 0.1 * dt);
        const fanSmallRpm = Math.round(base.fanSmallRpm + ((uxtuParams.fanSmallRpmTarget ?? 5200) - base.fanSmallRpm) * 0.1 * dt);
        const fanLargePct = base.fanLargeMax > 0 ? fanLargeRpm / base.fanLargeMax : 0;
        const fanSmallPct = base.fanSmallMax > 0 ? fanSmallRpm / base.fanSmallMax : 0;
        const cooling = 0.4 * fanLargePct + 0.25 * fanSmallPct;
        const modeBias = settings.mode === "silent" ? -0.12 : settings.mode === "office" ? -0.05 : settings.mode === "gaming" ? 0.05 : settings.mode === "beast" ? 0.14 : 0;
        const cpuTargetUsage = Math.max(5, Math.min(95, 25 + (uxtuParams.cpuLongPptW / 120) * 55 + modeBias * 100));
        const gpuTargetUsage = Math.max(2, Math.min(95, 15 + (uxtuParams.gpuPptLimitW / 180) * 55 + modeBias * 80));
        const drift = (target, current, strength) => current + (target - current) * strength * dt + (Math.random() - 0.5) * 1.5;
        const nextCpuUsage = drift(cpuTargetUsage, base.cpuUsage, 0.18);
        const nextGpuUsage = drift(gpuTargetUsage, base.gpuUsage, 0.16);
        return {
          ...base,
          cpuUsage: Math.round(nextCpuUsage),
          cpuFreq: Number(Math.max(0.6, base.cpuFreq + (nextCpuUsage - base.cpuUsage) * 0.02).toFixed(2)),
          cpuTemp: Math.round(drift(uxtuParams.cpuTempLimitC - cooling * 12, base.cpuTemp, 0.10)),
          gpuUsage: Math.round(nextGpuUsage),
          gpuFreq: Number(Math.max(0.4, base.gpuFreq + (nextGpuUsage - base.gpuUsage) * 0.03).toFixed(2)),
          gpuTemp: Math.round(drift(uxtuParams.gpuTempLimitC - cooling * 10, base.gpuTemp, 0.09)),
          memoryUsage: Math.round(Math.max(1, Math.min(99, base.memoryUsage + (Math.random() - 0.5) * 0.8))),
          diskUsage: Math.round(Math.max(1, Math.min(99, base.diskUsage + (Math.random() - 0.5) * 0.6))),
          fanLargeRpm, fanSmallRpm,
        };
      });
    }, 1000);
    return () => clearInterval(timer);
  }, [settings.mode, uxtuParams, backendOnline]);

  // ── Overrides 稀疏存储操作（仅更新 React state，持久化由各独立端点完成） ──
  const saveOverrideFn = useCallback((mode, key, value) => {
    setOverrides(prev => ({ ...prev, [key]: value }));
  }, []);

  const clearOverrideFn = useCallback(async (mode, fields) => {
    if (!fields?.length) return;
    await clearOverrides(mode, fields);
    const fieldSet = new Set(fields);
    setOverrides(prev => {
      const next = { ...prev };
      for (const k of fieldSet) delete next[k];
      return next;
    });
    setUxtuParams(prev => {
      const next = { ...prev };
      for (const k of fieldSet) {
        if (k in FULL_PARAMS) next[k] = FULL_PARAMS[k];
        else delete next[k];
      }
      return next;
    });
  }, []);

  // Switch profile (Dashboard Dock / ConfigBar calls this)
  const switchProfile = useCallback((profileId) => {
    setSettings(prev => prev.mode === profileId ? prev : { ...prev, mode: profileId });
    setCurrentProfile(profiles.find(p => p.id === profileId) || null);
  }, [profiles]);

  // After profile deleted
  const afterProfileDeleted = useCallback((deletedId) => {
    setProfiles(prev => prev.filter(p => p.id !== deletedId));
    setCurrentProfile(prev => (prev && prev.id === deletedId) ? null : prev);
  }, []);

  // After profile created
  const afterProfileCreated = useCallback((entry) => {
    setProfiles(prev => [...prev, entry]);
    setCurrentProfile(entry);
    setSettings(prev => ({ ...prev, mode: entry.id }));
  }, []);

  // 重新拉取当前配置并同步到 UI（恢复出厂/导入后使用）
  const refreshOverrides = useCallback(async () => {
    try {
      const { mode, overrides: rawOv } = await fetchOverrides();
      const ov = flattenBackendOverrides(rawOv, _maxCores);
      prevModeRef.current = mode;
      setSettings(prev => ({ ...prev, mode }));
      const fanDefaults = MODE_FAN_DEFAULTS[mode] || {};
      setUxtuParams({ ...FULL_PARAMS, ...fanDefaults, ...ov });
      setOverrides(ov);
      setCurrentProfile(profiles.find(p => p.id === mode) || null);
    } catch (e) {
      console.error('[useControlState] refresh overrides error:', e);
    }
  }, [profiles]);

  return {
    theme, setTheme,
    telemetry, setTelemetry,
    uxtuParams, setUxtuParams,
    settings, setSettings,
    history,
    overrides, setOverrides,
    saveOverride: saveOverrideFn,
    clearOverride: clearOverrideFn,
    resetParams,
    switching,
    backendOnline,
    profiles, setProfiles,
    currentProfile, setCurrentProfile,
    platformInfo,
    platformInfoReady,
    switchProfile,
    afterProfileDeleted,
    afterProfileCreated,
    refreshOverrides,
  };
}
