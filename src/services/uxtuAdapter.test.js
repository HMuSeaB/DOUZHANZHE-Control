import { describe, expect, it } from "vitest";
import {
  clampParam,
  getFanRange,
  FULL_FAN_RANGE,
  FULL_PARAMS,
  MODE_FAN_DEFAULTS,
  PARAM_RANGES,
  powerPlanHALMap,
} from "./uxtuAdapter";

const MODES = ["silent", "office", "gaming", "beast"];

describe("clampParam", () => {
  it("把超出上下限的值钳到边界", () => {
    expect(clampParam("cpuTempLimitC", 200)).toBe(100);
    expect(clampParam("cpuTempLimitC", 10)).toBe(60);
  });

  it("区间内的值原样返回", () => {
    expect(clampParam("cpuLongPptW", 55)).toBe(55);
  });

  it("电压偏移只允许降压，正值会被钳到 0", () => {
    expect(clampParam("cpuVoltageOffset", 20)).toBe(0);
    expect(clampParam("cpuVoltageOffset", -50)).toBe(-30);
  });

  it("未知参数不做钳位，原样透传", () => {
    expect(clampParam("notARealParam", 99999)).toBe(99999);
  });

  it("每个已知参数的钳位结果都落在自身区间内", () => {
    for (const [key, { min, max }] of Object.entries(PARAM_RANGES)) {
      expect(clampParam(key, min - 1000), `${key} 下越界`).toBe(min);
      expect(clampParam(key, max + 1000), `${key} 上越界`).toBe(max);
    }
  });
});

describe("PARAM_RANGES", () => {
  it("每个区间的 min 都不大于 max", () => {
    for (const [key, { min, max }] of Object.entries(PARAM_RANGES)) {
      expect(min, `${key} 的 min 应不大于 max`).toBeLessThanOrEqual(max);
    }
  });

  it("FULL_PARAMS 里受钳位约束的默认值都在合法区间内", () => {
    for (const [key, { min, max }] of Object.entries(PARAM_RANGES)) {
      if (!(key in FULL_PARAMS)) continue;
      const value = FULL_PARAMS[key];
      expect(value, `${key} 默认值 ${value} 超出 [${min}, ${max}]`).toBeGreaterThanOrEqual(min);
      expect(value, `${key} 默认值 ${value} 超出 [${min}, ${max}]`).toBeLessThanOrEqual(max);
    }
  });
});

describe("getFanRange", () => {
  it.each(MODES)("%s 模式的区间 min 不大于 max", (mode) => {
    const r = getFanRange(mode);
    expect(r.largeMin).toBeLessThanOrEqual(r.largeMax);
    expect(r.smallMin).toBeLessThanOrEqual(r.smallMax);
  });

  it("未知模式回退到最保守的安静模式，避免误用高转速区间", () => {
    expect(getFanRange("nonexistent")).toEqual(getFanRange("silent"));
  });

  it.each(MODES)("%s 模式的区间被 FULL_FAN_RANGE 覆盖", (mode) => {
    const r = getFanRange(mode);
    expect(r.largeMin).toBeGreaterThanOrEqual(FULL_FAN_RANGE.largeMin);
    expect(r.largeMax).toBeLessThanOrEqual(FULL_FAN_RANGE.largeMax);
    expect(r.smallMin).toBeGreaterThanOrEqual(FULL_FAN_RANGE.smallMin);
    expect(r.smallMax).toBeLessThanOrEqual(FULL_FAN_RANGE.smallMax);
  });
});

describe("MODE_FAN_DEFAULTS", () => {
  it.each(MODES)("%s 模式的默认转速落在该模式区间内", (mode) => {
    const d = MODE_FAN_DEFAULTS[mode];
    const r = getFanRange(mode);
    expect(d.fanLargeRpmTarget).toBeGreaterThanOrEqual(r.largeMin);
    expect(d.fanLargeRpmTarget).toBeLessThanOrEqual(r.largeMax);
    expect(d.fanSmallRpmTarget).toBeGreaterThanOrEqual(r.smallMin);
    expect(d.fanSmallRpmTarget).toBeLessThanOrEqual(r.smallMax);
  });
});

describe("powerPlanHALMap", () => {
  it("各电源计划的取值互不重复", () => {
    const values = Object.values(powerPlanHALMap);
    expect(new Set(values).size).toBe(values.length);
  });
});
