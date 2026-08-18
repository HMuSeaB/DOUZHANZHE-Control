import { describe, it, expect } from 'vitest'
import { flattenBackendOverrides, powerPlanHALMap } from '../uxtuAdapter'

// ── flattenBackendOverrides: powerPlan 整数 → 字符串 id 归一化 ──
// 回归说明（修的是切到控制面板时「电源管理」已保存的档位不显示）：
//   后端 overrides.powerPlan 存的是 HAL 整数(0/1/2)，前端按钮比较用的是字符串 id
//   (balance/performance/efficiency)。若 flatten 直接把整数塞进 cpuPowerPlan，
//   则 uxtuParams.cpuPowerPlan === plan.id 永远不成立 → 切过去选中项空白。
//   这里必须在 flatten 时把整数归一成 id。

describe('flattenBackendOverrides powerPlan', () => {
  it('把后端整数 powerPlan 归一成前端字符串 id', () => {
    const HAL_TO_ID = Object.entries(powerPlanHALMap).reduce((acc, [id, hal]) => {
      acc[hal] = id
      return acc
    }, {})

    // 遍历所有合法 HAL 值，断言归一正确
    for (const [hal, id] of Object.entries(HAL_TO_ID)) {
      const flat = flattenBackendOverrides({ powerPlan: Number(hal) })
      expect(flat.cpuPowerPlan).toBe(id)
    }
  })

  it('已是字符串 id 时不重复映射（幂等）', () => {
    const flat = flattenBackendOverrides({ powerPlan: 'performance' })
    expect(flat.cpuPowerPlan).toBe('performance')
  })

  it('无 powerPlan 时不写入 cpuPowerPlan', () => {
    const flat = flattenBackendOverrides({ cpu: { freqLimitMhz: 3000 } })
    expect(flat.cpuPowerPlan).toBeUndefined()
  })
})
