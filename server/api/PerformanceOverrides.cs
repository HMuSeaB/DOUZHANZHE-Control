// SPDX-License-Identifier: GPL-3.0-only
//
// 性能设置持久化模型 — 由 Program.cs 抽出, 供 ProfileService 与单测共用。
// 字段序列化(JsonOpts camelCase + IncludeFields), 注意保持 public field 风格。

using System.Text.Json.Serialization;

namespace Douzhanzhe.API;

public class CpuOverrides { public int? FreqLimitMhz; public bool? TurboEnabled; public int? CoreLimitPercent; }
public class GpuOverrides { public int? CoreFreqMhz; public bool? FreqLocked; public int? MemFreqLevel; }
public class NvapiOverrides { public int? OcCoreOffsetMhz; public int? OcMemOffsetMhz; public int? PowerLimitW; public float? ThermalLimitC; }
public class SmuOverrides { public int? StapmLimitW; public int? ShortPowerLimitW; public int? TempLimitC; public int? CoAll; }
public class FanOverrides { public int? LargeRpm; public int? SmallRpm; }
public class PerformanceOverrides { public CpuOverrides Cpu = new(); public GpuOverrides Gpu = new(); public NvapiOverrides Nvapi = new(); public SmuOverrides Smu = new(); public FanOverrides Fan = new(); public int? PowerPlan; }
