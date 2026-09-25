// 风扇调速持久化探针 —— 直接打 API，绕开 UI，用来判定「设定值有没有真的落盘」。
//
// 它做的事：
//   1. 清掉 overrides 里的风扇两项（干净起点）
//   2. 写一个目标转速 → 回读 overrides 看是否落盘（注意：会被模式区间钳位）
//   3. 调 /api/overrides/switch（模拟「切换模式」）→ 再回读，确认没被清掉
//   4. 调 /api/fan/status 看实际生效值
//   5. 收尾清理
//
// 用法：
//   node tools/probe-fan.js                 # 默认打 3100（安装版）
//   node tools/probe-fan.js --port 3101
//   node tools/probe-fan.js --mode cfg-office --rpm 3600
//   node tools/probe-fan.js --keep          # 收尾不清理，留着让 UI 上肉眼确认
//
// 说明：令牌文件按端口隔离 → session-<port>.token（3100 就是 session-3100.token）。
//       Origin 头会被同源守卫用作白名单校验，所以每个请求都带上。

import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const argv = process.argv.slice(2);
const arg = (name, dflt) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 && argv[i + 1] && !argv[i + 1].startsWith("--") ? argv[i + 1] : dflt;
};
const flag = (name) => argv.includes(`--${name}`);

const PORT = arg("port", "3100");
const MODE = arg("mode", "cfg-office");
const RPM = Number(arg("rpm", "3600"));
const KEEP = flag("keep");

const BASE = `http://127.0.0.1:${PORT}`;
const APP_DIR = path.join(os.homedir(), "AppData", "Local", "Douzhanzhe Console");

// ---- 读令牌（按端口隔离；回退旧文件名） ----
let token = "";
for (const name of [`session-${PORT}.token`, "session.token"]) {
  const p = path.join(APP_DIR, name);
  try {
    token = fs.readFileSync(p, "utf8").trim();
    if (token) { console.log(`令牌来源：${name}（长度 ${token.length}）`); break; }
  } catch { /* 不存在，继续 */ }
}
if (!token) console.log("⚠ 没读到令牌文件，将只靠 Origin 头过守卫");

const HDRS = {
  Origin: BASE,
  Referer: BASE + "/",
  "Content-Type": "application/json",
  ...(token ? { "X-Douzhanzhe-Token": token } : {}),
};

const call = async (method, ep, body) => {
  try {
    const r = await fetch(BASE + ep, {
      method,
      headers: HDRS,
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await r.text();
    let json = null;
    try { json = JSON.parse(text); } catch { /* 非 JSON（HTML 错误页） */ }
    return { status: r.status, json, text };
  } catch (e) {
    return { status: 0, json: null, text: e.message };
  }
};

const fanOf = (r) => r?.json?.overrides?.fan ?? null;
const brief = (r) => `[${r.status}] ${JSON.stringify(r.json ?? r.text.slice(0, 140))}`;
let failures = 0;
const check = (label, ok, detail) => {
  if (ok) console.log(`  ✓ ${label}`);
  else { console.log(`  ✗ ${label}${detail ? " — " + detail : ""}`); failures++; }
};

(async () => {
  console.log(`\n目标：${BASE}   模式：${MODE}   写入目标：${RPM} RPM\n`);

  const h = await call("GET", "/api/health");
  console.log(`0. 存活检查 /api/health → ${brief(h)}`);
  if (h.status !== 200) {
    console.log("\n后端没响应。先确认 Douzhanzhe.Shell.exe / Douzhanzhe.API.exe 在跑。");
    process.exit(1);
  }

  console.log(`\n1. 清理 overrides.fan（干净起点）`);
  console.log(`   ${brief(await call("POST", "/api/overrides/clear", { mode: MODE, fields: ["fanLargeRpmTarget", "fanSmallRpmTarget"] }))}`);
  const before = fanOf(await call("GET", "/api/overrides"));
  console.log(`   清理后 fan = ${JSON.stringify(before)}`);

  console.log(`\n2. 写入 fanLargeRpmTarget = ${RPM}（mode=${MODE}）`);
  const setRes = await call("POST", `/api/fan/set-target?mode=${MODE}`, { largeRpm: RPM });
  console.log(`   ${brief(setRes)}`);
  check("set-target 返回 200", setRes.status === 200, `实际 ${setRes.status}`);

  console.log(`\n3. 回读 overrides —— 判定是否落盘`);
  const ov1 = fanOf(await call("GET", "/api/overrides"));
  console.log(`   fan = ${JSON.stringify(ov1)}`);
  const stored = ov1?.fanLargeRpmTarget ?? null;
  check("已落盘（非 null）", stored != null);
  if (stored != null && stored !== RPM) {
    console.log(`   ℹ 写入 ${RPM} 被钳位成 ${stored}（模式区间限制，属正常）`);
  }
  if (stored === 25249 || stored > 4400) {
    console.log(`   ✗ 落盘值 ${stored} 超出大扇物理上限 4400 —— 异常`);
    failures++;
  }

  console.log(`\n4. 模拟「切换模式」后回读（这是原 bug 的触发点）`);
  console.log(`   switch → ${brief(await call("POST", "/api/overrides/switch", { mode: MODE }))}`);
  const ov2 = fanOf(await call("GET", "/api/overrides"));
  console.log(`   fan = ${JSON.stringify(ov2)}`);
  check("切换后设定值未丢失", ov2?.fanLargeRpmTarget === stored,
        `切换前 ${stored} → 切换后 ${ov2?.fanLargeRpmTarget}`);

  console.log(`\n5. /api/fan/status（实际生效值）`);
  console.log(`   ${brief(await call("GET", "/api/fan/status"))}`);

  console.log(`\n6. /api/telemetry 里的风扇读数`);
  const tel = await call("GET", "/api/telemetry");
  if (tel.json) {
    const pick = (o, keys) => keys.filter((k) => k in (o ?? {})).map((k) => `${k}=${o[k]}`).join("  ");
    console.log(`   ${pick(tel.json, ["fanLargeRpm", "fanSmallRpm", "cpuFanRpm", "gpuFanRpm", "fanLargeMax", "fanSmallMax"]) || JSON.stringify(tel.json).slice(0, 200)}`);
    const lr = tel.json.fanLargeRpm ?? tel.json.cpuFanRpm;
    if (lr != null) {
      check(`大扇读数 ${lr} 在物理上限内`, lr <= (tel.json.fanLargeMax ?? 4400), "疑似脏读");
    }
  } else {
    console.log(`   ${brief(tel)}`);
  }

  if (!KEEP) {
    console.log(`\n7. 收尾清理`);
    console.log(`   ${brief(await call("POST", "/api/overrides/clear", { mode: MODE, fields: ["fanLargeRpmTarget", "fanSmallRpmTarget"] }))}`);
  } else {
    console.log(`\n7. --keep：保留设定值 ${stored}，去 UI 上确认它显示为你设的数、且刷新后不变`);
  }

  console.log(`\n${failures === 0 ? "全部通过 ✓" : `${failures} 项未通过 ✗`}`);
  console.log(`提示：这些检查只证明「API 层落盘与钳位」正确；`);
  console.log(`      实机风扇是否真的转到该转速、EC 读数是否干净，要配合日志观察（tools\\watch-applog.ps1）。\n`);
  process.exit(failures === 0 ? 0 : 1);
})();
