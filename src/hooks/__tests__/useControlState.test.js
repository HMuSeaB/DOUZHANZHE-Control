import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor, act, cleanup } from '@testing-library/react'
import { useControlState } from '../useControlState'
import {
  fetchOverrides,
  fetchProfiles,
  fetchPlatformInfo,
  fetchTelemetry,
  createTelemetrySocket,
  migrateLocalStorageOverrides,
  FULL_PARAMS,
} from '../../services/uxtuAdapter'

// ── 基线契约测试（useControlState） ──
//
// 目标：在共享 store 改造（docs/v2.0/tab-switch-shared-state.md Option A）前后，
// 用同一套用例保证对外行为不回归。这些用例断言 hook「对外契约」：
//   - 启动后按预期填充 settings / uxtuParams / overrides / profiles / currentProfile
//   - switchProfile 能改 settings.mode
//   - saveOverride / setOverrides 能同步 overrides 与 uxtuParams
//   - WebSocket 遥测能更新 telemetry
//   - 后端失败时回退 FULL_PARAMS 默认（backendOnline 保持关闭）
//
// 只 mock 「网络」层（fetch / WebSocket / 迁移 / ui-state），
// 保留 FULL_PARAMS / MODE_FAN_DEFAULTS / flattenBackendOverrides 等真实纯逻辑。

// 共享容器：在 vi.mock factory 与测试体之间传递「最近一次 websocket onData」回调，
// 以及固定的内置配置样例（避免 hoist 提升导致的 TDZ 引用错误）
const { socketHolder, MOCK_PROFILE } = vi.hoisted(() => ({
  socketHolder: { lastOnData: null },
  MOCK_PROFILE: { id: 'office', name: '均衡模式', builtIn: true },
}))

vi.mock('../../services/uxtuAdapter', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    migrateLocalStorageOverrides: vi.fn().mockResolvedValue(0),
    fetchPlatformInfo: vi.fn().mockResolvedValue({
      oem: 'Bellator', vendor: 'Intel', model: 'SSD3',
      isElevated: true,
    }),
    fetchProfiles: vi.fn().mockResolvedValue({ profiles: [MOCK_PROFILE] }),
    fetchTelemetry: vi.fn().mockResolvedValue({ cpuUsage: 11, gpuUsage: 22 }),
    fetchOverrides: vi.fn().mockResolvedValue({ mode: 'office', overrides: {} }),
    fetchUiState: vi.fn().mockResolvedValue({}),
    saveUiState: vi.fn().mockResolvedValue({}),
    createTelemetrySocket: vi.fn((onData) => {
      socketHolder.lastOnData = onData
      return { close: vi.fn() }
    }),
  }
})

describe('useControlState 对外契约', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    socketHolder.lastOnData = null
    // 还原这些函数到已知默认实现，避免跨用例污染
    migrateLocalStorageOverrides.mockResolvedValue(0)
    fetchPlatformInfo.mockResolvedValue({
      oem: 'Bellator', vendor: 'Intel', model: 'SSD3', isElevated: true,
    })
    fetchProfiles.mockResolvedValue({ profiles: [MOCK_PROFILE] })
    fetchTelemetry.mockResolvedValue({ cpuUsage: 11, gpuUsage: 22 })
    fetchOverrides.mockResolvedValue({ mode: 'office', overrides: {} })
    createTelemetrySocket.mockImplementation((onData) => {
      socketHolder.lastOnData = onData
      return { close: vi.fn() }
    })
    // 原始 fetch(/api/platform/info)（hook 内直接调用）也要可控
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ vendor: 'Intel' }),
    })
  })

  afterEach(() => {
    // 显式卸载已挂载的 hook，中止其 WebSocket/定时器副作用，
    // 避免用例间互相泄漏（Vitest globals=false 时 RTL 不会自动 cleanup）
    cleanup()
  })

  it('启动后：settings.mode 取自 fetchOverrides，overrides 与 uxtuParams 被填充', async () => {
    fetchOverrides.mockResolvedValueOnce({
      mode: 'gaming',
      overrides: { cpu: { freqLimitMhz: 3000 } },
    })

    const { result } = renderHook(() => useControlState())

    await waitFor(() => {
      expect(result.current.settings.mode).toBe('gaming')
    })
    expect(result.current.overrides).toHaveProperty('cpuFreqLimitMhz', 3000)
    expect(result.current.uxtuParams.cpuFreqLimitMhz).toBe(3000)
    // 启动拉取只应发生一次
    expect(fetchOverrides).toHaveBeenCalledTimes(1)
  })

  it('profiles / currentProfile 与 platformInfo 被正确填充', async () => {
    const { result } = renderHook(() => useControlState())

    await waitFor(() => {
      expect(result.current.platformInfoReady).toBe(true)
    })
    expect(result.current.platformInfo.oem).toBe('Bellator')
    expect(result.current.profiles).toEqual([MOCK_PROFILE])
    expect(result.current.currentProfile?.id).toBe('office')
  })

  it('switchProfile 会切换 settings.mode 并更新 currentProfile', async () => {
    fetchProfiles.mockResolvedValueOnce({
      profiles: [
        MOCK_PROFILE,
        { id: 'beast', name: '高性能', builtIn: true },
      ],
    })

    const { result } = renderHook(() => useControlState())

    await waitFor(() => {
      expect(result.current.profiles).toHaveLength(2)
    })

    await act(async () => {
      result.current.switchProfile('beast')
    })
    expect(result.current.settings.mode).toBe('beast')
    expect(result.current.currentProfile?.id).toBe('beast')
  })

  it('saveOverride 只广播 overrides 灰显态；setUxtuParams 单独驱动 uxtuParams 值', async () => {
    const { result } = renderHook(() => useControlState())

    await waitFor(() => {
      expect(result.current.settings.mode).toBe('office')
    })

    act(() => {
      result.current.saveOverride('office', 'cpuLongPptW', 80)
    })
    // saveOverride 仅维护 overrides（UI 高亮/灰显用），不直接改 uxtuParams
    expect(result.current.overrides.cpuLongPptW).toBe(80)
    expect(result.current.uxtuParams.cpuLongPptW).toBe(FULL_PARAMS.cpuLongPptW)

    // uxtuParams 的值由面板组件通过 setUxtuParams 单独驱动
    act(() => {
      result.current.setUxtuParams({ ...result.current.uxtuParams, cpuLongPptW: 80 })
    })
    expect(result.current.uxtuParams.cpuLongPptW).toBe(80)

    act(() => {
      result.current.setOverrides({ cpuLongPptW: 60 })
    })
    expect(result.current.overrides.cpuLongPptW).toBe(60)
  })

  it('WebSocket 遥测会更新 telemetry', async () => {
    const { result } = renderHook(() => useControlState())

    await waitFor(() => {
      // 初始遥测经 fetchTelemetry 填充
      expect(result.current.telemetry?.cpuUsage).toBe(11)
    })

    // 模拟后端经 WebSocket 推送一条实时遥测
    expect(socketHolder.lastOnData).toBeTypeOf('function')
    act(() => {
      socketHolder.lastOnData({ cpuUsage: 55, gpuUsage: 66, gpuMode: 1 })
    })
    expect(result.current.telemetry.cpuUsage).toBe(55)
    expect(result.current.telemetry.gpuUsage).toBe(66)
  })

  it('后端失败时回退到 FULL_PARAMS 默认，且不抛错', async () => {
    fetchOverrides.mockRejectedValueOnce(new Error('backend offline'))
    fetchTelemetry.mockRejectedValueOnce(new Error('offline'))
    fetchPlatformInfo.mockRejectedValueOnce(new Error('offline'))
    fetchProfiles.mockRejectedValueOnce(new Error('offline'))

    const { result } = renderHook(() => useControlState())

    // 等待启动副作用收敛（即使全部失败也不应 unhandled rejection）
    // fetchOverrides 被调用即代表启动流程已推进到配置拉取这一步
    await waitFor(() => {
      expect(fetchOverrides).toHaveBeenCalled()
    })

    // 失败回退：overrides 为空，uxtuParams 使用 FULL_PARAMS 兆底
    expect(result.current.overrides).toEqual({})
    expect(result.current.uxtuParams).toHaveProperty('cpuLongPptW')
    expect(result.current.settings.mode).toBeDefined()
  })
})
