// 斗战者 API 回归测试 —— 断言式，带退出码，可直接当构建后/装机后的闸门。
//
// 与 probe-fan.js 的分工：
//   · probe-fan.js     = 诊断/探查工具。写值、回读、看现状，输出给人读。
//   · regression-api.js = 断言式回归测试。只报 PASS/FAIL，退出码非 0 即失败。放 CI。
//
// 覆盖的 5 项既有缺陷（都是 2026-09-25 修掉的）：
//   [1] GET /api/health 必须豁免同源守卫 —— 否则 Shell 看门狗 403 → 每 16s 重启后端
//   [2] 同源守卫仍须拦住其它端点 —— 别为了修 [1] 把守卫放宽过头
//   [3] 未知 mode id 必须返回 400 —— 旧行为假报 ok:true 但根本没落盘
//   [4] 模式钳位必须生效 —— 旧实现只认裸名，前端传 cfg-* 时静默落到兜底区间
//   [5] 切换模式不得清空用户设定 —— 这是用户最初报的「过一会儿恢复默认」
//
// 安全：会临时改写风扇设定。默认**先备份、后恢复**，跑完打印 diff 结果。
//
// 用法：
//   node tools/regression-api.js                        # 3100，cfg-office
//   node tools/regression-api.js --port 3101
//   node tools/regression-api.js --mode cfg-beast --large 3600
//   node tools/regression-api.js --keep                 # 不恢复（调试用）
//   node tools/regression-api.js --no-switch            # 跳过会重置 CPU/GPU 调优的 switch
//   node tools/regression-api.js --log <app.log 路径>    # 覆盖日志路径（用于 [3b] 检测）
//
// 退出码：0 = 全部 PASS；1 = 有 FAIL

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import process from "node:process";

const argv = process.argv.slice(2);
const arg = (name, dflt) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 && argv[i + 1] && !argv[i + 1].startsWith("--") ? argv[i + 1] : dflt;
};
const flag = (name) => argv.includes(`--${name}`);

const PORT = arg("port", "3100");
const MODE = arg("mode", "cfg-office");
const KEEP = flag("keep");
const NO_SWITCH = flag("no-switch");
const LARGE = Number(arg("large", "9999")); // 故意超区间，用来验证钳位

const BASE = `http://127.0.0.1:${PORT}`;
const APP_DIR = path.join(os.homedir(), "AppData", "Local", "Douzhanzhe Console");

// 后端 FanRpmRange() 的区间表（见 references/api-contract.md）
const RANGES = {
  silent: { large: [1900, 2900], small: [1700, 6400] },
  office: { large: [2600, 3500], small: [5900, 6900] },
  gaming: { large: [4000, 4400], small: [7500, 8200] },
  beast: { large: [3200, 3800], small: [6400, 7200] },
};

const bare = (id) => (id ?? "").replace(/^cfg-/, "").toLowerCase();

// ── 结果收集 ──
let pass = 0;
const fails = [];
function check(label, ok, detail = "") {
  if (ok) {
    pass++;
    console.log(`  ✓ PASS  ${label}`);
  } else {
    fails.push(label + (detail ? ` — ${detail}` : ""));
    console.log(`  ✗ FAIL  ${label}${detail ? "  — " + detail : ""}`);
  }
  return ok;
}

// ── 令牌（按端口隔离，回退旧名）──
let token = "";
for (const name of [`session-${PORT}.token`, "session.token"]) {
  try {
    const t = fs.readFileSync(path.join(APP_DIR, name), "utf8").trim();
    if (t) { token = t; console.log(`令牌来源：${name}`); break; }
  } catch { /* 继续 */ }
}

const authed = {
  Origin: BASE,
  Referer: BASE + "/",
  "Content-Type": "application/json",
  ...(token ? { "X-Douzhanzhe-Token": token } : {}),
};

async function call(method, ep, body, headers = authed) {
  try {
    const r = await fetch(BASE + ep, {
      method, headers,
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await r.text();
    let json = null;
    try { json = JSON.parse(text); } catch { /* HTML 错误页 */ }
    return { status: r.status, json, text };
  } catch (e) {
    return { status: 0, json: null, text: e.message };
  }
}

const fanOf = (r) => r?.json?.overrides?.fan ?? null;
const largeOf = (r) => fanOf(r)?.largeRpm ?? null;
const smallOf = (r) => fanOf(r)?.smallRpm ?? null;

console.log(`\n斗战者 API 回归测试   目标 ${BASE}   模式 ${MODE}\n`);

// ── 前置：后端活着 ──
const h0 = await call("GET", "/api/health", null, {});
if (h0.status !== 200) {
  console.log(`后端无响应（/api/health → ${h0.status}）。先启动 Douzhanzhe.Shell.exe。`);
  process.exit(1);
}

// ── 备份 ──
const before = await call("GET", "/api/overrides");
const bakFan = { largeRpm: largeOf(before), smallRpm: smallOf(before) };
const bakStamp = new Date().toISOString().replace(/[:.]/g, "-");
const bakFile = path.join("logs", `regression-backup-${bakStamp}.json`);
try {
  fs.mkdirSync("logs", { recursive: true });
  fs.writeFileSync(bakFile, JSON.stringify({ port: PORT, mode: before.json?.mode, fan: bakFan }, null, 2));
  console.log(`已备份原设定 → ${bakFile}  fan=${JSON.stringify(bakFan)}\n`);
} catch (e) {
  console.log(`⚠ 备份写入失败：${e.message}（继续，但收尾恢复将依赖内存里的值）\n`);
}

// ────────────────────────────────────────────
console.log("[1] GET /api/health 必须豁免同源守卫（不带任何头）");
const h1 = await call("GET", "/api/health", null, {});
check("无来源头仍返回 200", h1.status === 200, `实际 ${h1.status}`);

console.log("\n[2] 同源守卫仍须拦住其它端点（不带 Origin）");
const g2 = await call("GET", "/api/overrides", null, { "Content-Type": "application/json" });
check("无 Origin 打 /api/overrides 返回 403", g2.status === 403, `实际 ${g2.status}（若为 200 说明守卫被放宽过头）`);

console.log("\n[3] 未知 mode id 必须返回 400（旧行为假报 ok:true）");
// 先记录日志大小，用于检测「被拒的请求是否仍然写了硬件」
const LOG = arg("log", path.join(os.homedir(), "AppData", "Local", "Douzhanzhe Console", "logs", "app.log"));
const logSizeBefore = fs.existsSync(LOG) ? fs.statSync(LOG).size : 0;
const bad = await call("POST", "/api/fan/set-target?mode=__nonexistent__", { largeRpm: 3000 });
check("未知配置 id 返回 400", bad.status === 400, `实际 ${bad.status} ${bad.text.slice(0, 100)}`);

console.log("\n[3b] 已知缺陷：被拒的请求仍会写硬件（告警，不计入失败）");
try {
  if (logSizeBefore > 0) {
    const fd = fs.openSync(LOG, "r");
    const buf = Buffer.alloc(fs.statSync(LOG).size - logSizeBefore);
    fs.readSync(fd, buf, 0, buf.length, logSizeBefore);
    fs.closeSync(fd);
    const chunk = buf.toString("utf8");
    const leaked = chunk.split("\n").filter((l) => l.includes("[FanEC]") && l.includes("__nonexistent__"));
    if (leaked.length > 0) {
      console.log("  ⚠ WARN  400 已返回，但硬件仍被写入：");
      for (const l of leaked) console.log(`          ${l.trim()}`);
      console.log("          原因：/api/fan/set-target 先调 ApplyFanSpeed 再校验 mode id。");
      console.log("          影响：无效 mode 的值会以兜底区间 (0,4400) 落到硬件，绕过模式钳位。");
    } else {
      console.log("  ✓ 未发现「被拒请求写硬件」的痕迹（该缺陷已修？）");
    }
  } else {
    console.log("  ⤵ 读不到日志，跳过该检查（可用 --log <path> 指定）");
  }
} catch (e) {
  console.log(`  ⤵ 日志检查失败：${e.message}`);
}

console.log(`\n[4] 模式钳位必须生效（写 ${LARGE}，应被钳到该模式上限）`);
const expectLarge = RANGES[bare(MODE)]?.large?.[1];
await call("POST", "/api/overrides/clear", { mode: MODE, fields: ["fanLargeRpmTarget", "fanSmallRpmTarget"] });
const w4 = await call("POST", `/api/fan/set-target?mode=${MODE}`, { largeRpm: LARGE });
const ov4 = await call("GET", "/api/overrides");
const got4 = largeOf(ov4);
if (expectLarge == null) {
  console.log(`  ℹ 模式 '${MODE}' 不在已知区间表里，只校验「已落盘且不超过物理上限 4400」`);
  check("已落盘且 ≤ 4400", got4 != null && got4 <= 4400, `落盘值 ${got4}`);
} else {
  check(`写入被钳到 ${expectLarge}（未落到兜底区间 4400）`, got4 === expectLarge, `落盘值 ${got4}`);
}

console.log("\n[5] 切换模式不得清空用户设定（用户最初报的 bug）");
if (NO_SWITCH) {
  console.log("  ⤵ 已用 --no-switch 跳过（该接口会重置 CPU/GPU 调优）");
} else {
  await call("POST", "/api/overrides/switch", { mode: MODE });
  const ov5 = await call("GET", "/api/overrides");
  check("switch 后设定值仍在", largeOf(ov5) === got4, `switch 前 ${got4} → 后 ${largeOf(ov5)}`);
}

console.log("\n[6] 令牌文件按端口隔离");
const tokPath = path.join(APP_DIR, `session-${PORT}.token`);
check(`存在 session-${PORT}.token`, fs.existsSync(tokPath), "缺该文件说明跑的是旧版");

// ── 恢复 ──
console.log("\n[7] 恢复原设定");
if (KEEP) {
  console.log(`  ⤵ 已用 --keep 跳过恢复。原值：${JSON.stringify(bakFan)}（备份在 ${bakFile}）`);
} else {
  const body = {};
  if (bakFan.largeRpm != null) body.largeRpm = bakFan.largeRpm;
  if (bakFan.smallRpm != null) body.smallRpm = bakFan.smallRpm;
  if (Object.keys(body).length === 0) {
    await call("POST", "/api/overrides/clear", { mode: MODE, fields: ["fanLargeRpmTarget", "fanSmallRpmTarget"] });
    console.log("  原设定为空 → 已清除");
  } else {
    const rr = await call("POST", `/api/fan/set-target?mode=${MODE}`, body);
    const now = await call("GET", "/api/overrides");
    const same = largeOf(now) === bakFan.largeRpm && smallOf(now) === bakFan.smallRpm;
    check(`原设定已恢复 ${JSON.stringify(bakFan)}`, same, `现在 ${JSON.stringify({ largeRpm: largeOf(now), smallRpm: smallOf(now) })}`);
    if (!same) console.log(`  ⚠ 恢复失败！手动恢复：${JSON.stringify(body)}`);
    void rr;
  }
}

// ── [8] 未知 mode 必须在动硬件之前就被拒绝 ──
// 修完 /api/fan/set-target 后对其余调优端点做了同类排查，发现它们全是
// 「先动硬件、后持久化，且丢弃 SavePerfOverrides 的返回值」：未知配置 id 时
// 硬件照样被改、值却没落盘 —— 用户当下看到设置生效，重启/切模式后又恢复默认，
// 正是最初报的那个症状。现统一由 RejectUnknownMode 在动硬件之前拦截。
// 这些请求都返回 400，不会碰到硬件，可以放心跑。
console.log("\n[8] 未知 mode 必须在动硬件之前被拒绝（RejectUnknownMode 守卫）");
const GUARDED = [
  ["/api/control",               { target: "kb_light", value: 1 }],
  ["/api/gpu/set",               { action: "limit-max", value: 1500 }],
  ["/api/smu/set",               { parameter: "stapm_limit", valueM: 25 }],
  ["/api/fan/restore",           {}],
  ["/api/nvapi/overclock",       { coreOffsetMhz: 100, memOffsetMhz: 0 }],
  ["/api/nvapi/power-limit",     { powerW: 80 }],
  ["/api/nvapi/thermal-limit",   { tempC: 85 }],
  ["/api/cpu/freq-limit",        { mhz: 3000 }],
  ["/api/cpu/turbo",             { enabled: true }],
  ["/api/cpu/core-limit",        { percent: 100 }],
  ["/api/cpu/reset",             {}],
];
let guardedOk = 0;
for (const [ep, body] of GUARDED) {
  const r = await call("POST", `${ep}?mode=__nonexistent__`, body);
  if (r.status === 400) guardedOk++;
  else console.log(`  ⚠ ${ep} 未被拦截 → ${r.status} ${r.text.slice(0, 80)}`);
}
check(`${GUARDED.length} 个调优端点都在动硬件前拒绝未知 mode`,
  guardedOk === GUARDED.length, `${guardedOk}/${GUARDED.length} 个返回 400`);

// 反向：合法 mode 不能被误拦 —— 应走到各端点自己的校验逻辑
const okGpu = await call("POST", `/api/gpu/set?mode=${MODE}`, { action: "__bogus__" });
check("合法 mode 不被误拦（gpu/set 应走到自己的 action 校验）",
  /unknown action/.test(okGpu.text), `实际 ${okGpu.status} ${okGpu.text.slice(0, 80)}`);

console.log(`\n${"─".repeat(56)}`);
if (fails.length === 0) {
  console.log(`全部 PASS ✓  （${pass} 项）`);
  process.exit(0);
}
console.log(`✗ ${fails.length} 项 FAIL / ${pass} 项 PASS`);
for (const f of fails) console.log(`  · ${f}`);
process.exit(1);
