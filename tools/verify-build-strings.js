// 验证「构建产物里到底有没有把本次改动编进去」。
//
// 背景：本项目踩过一次 —— 代码改了、构建也"成功"了，但装出来的包行为没变。
// 事后才发现要靠检索二进制里的字符串来确认。这个脚本把那次的一次性验证固化下来。
//
// ⚠ .NET 的字符串分布在两个堆，编码不同，混用会得到假阴性：
//     · 方法名/类型名  → 元数据 #Strings 堆 → **UTF-8**    → 普通 grep 能抓到
//     · 字符串字面量   → #US 堆            → **UTF-16LE** → grep 搜中文一律抓不到
//   所以清单里 method 和 literal 分开列，脚本分别按 UTF-8 / UTF-16LE 检索。
//
// 用法：
//   node tools/verify-build-strings.js                 # 用 tools/build-manifest.json
//   node tools/verify-build-strings.js --manifest x.json
//   node tools/verify-build-strings.js --root <dir>     # 覆盖清单里的 root
//   node tools/verify-build-strings.js --quiet          # 只打印失败项与总结
//
// 退出码：0 = 全部命中；1 = 有缺失（可直接用于 CI / 构建后闸门）
//
// 前置：先跑过 installer/build-installer.ps1 或 dotnet publish，产物在 dist/publish/api。

import fs from "node:fs";
import path from "node:path";
import process from "node:process";

const argv = process.argv.slice(2);
const arg = (name, dflt) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 && argv[i + 1] && !argv[i + 1].startsWith("--") ? argv[i + 1] : dflt;
};
const flag = (name) => argv.includes(`--${name}`);

const MANIFEST = arg("manifest", path.join("tools", "build-manifest.json"));
const QUIET = flag("quiet");

if (!fs.existsSync(MANIFEST)) {
  console.error(`找不到清单文件：${MANIFEST}`);
  process.exit(1);
}
const manifest = JSON.parse(fs.readFileSync(MANIFEST, "utf8"));
const ROOT = arg("root", manifest.root ?? "dist/publish/api");

let pass = 0;
let fail = 0;
const failures = [];

/** 按指定编码检索 needle */
const has = (buf, needle, enc) => buf.includes(Buffer.from(needle, enc));

/** 检查一个二进制文件的 method(UTF-8) + literal(UTF-16LE) + mustNot 清单 */
function checkBinary(file, spec) {
  const full = path.join(ROOT, file);
  let buf;
  try {
    buf = fs.readFileSync(full);
  } catch (e) {
    console.log(`\n=== ${file}  [${spec.label ?? ""}] ===`);
    console.log(`  ✗ 读取失败：${e.message}`);
    fail++;
    failures.push(`${file} 读取失败`);
    return;
  }

  console.log(`\n=== ${file}  [${spec.label ?? ""}]  ${buf.length.toLocaleString()} bytes ===`);
  if (spec.note) console.log(`  ℹ ${spec.note}`);

  const rows = [];
  for (const n of spec.method ?? []) rows.push({ n, enc: "utf8", kind: "方法名" });
  for (const n of spec.literal ?? []) rows.push({ n, enc: "utf16le", kind: "字面量" });

  for (const { n, enc, kind } of rows) {
    const ok = has(buf, n, enc);
    if (ok) {
      pass++;
      if (!QUIET) {
        const cross = enc === "utf16le" && has(buf, n, "utf8") ? "  [UTF-8 也有]" : "";
        console.log(`  ✓ ${kind} ${JSON.stringify(n)}${cross}`);
      }
    } else {
      fail++;
      failures.push(`${file} 缺 ${kind} ${JSON.stringify(n)}`);
      console.log(`  ✗ ${kind} ${JSON.stringify(n)}  ← 缺失（${enc === "utf8" ? "UTF-8" : "UTF-16LE"} 检索）`);
    }
  }

  for (const n of spec.mustNot ?? []) {
    if (has(buf, n, "utf16le") || has(buf, n, "utf8")) {
      fail++;
      failures.push(`${file} 残留了不该有的 ${JSON.stringify(n)}`);
      console.log(`  ✗ 不该出现却存在：${JSON.stringify(n)}`);
    } else {
      pass++;
      if (!QUIET) console.log(`  ✓ 已移除 ${JSON.stringify(n)}`);
    }
  }
}

/** 检查前端 bundle（压缩后行数极少，必须按出现次数统计） */
function checkFrontend(fe) {
  const dir = fe.dir;
  if (!fs.existsSync(dir)) {
    console.log(`\n=== 前端 bundle ===`);
    console.log(`  ✗ 目录不存在：${dir}`);
    fail++;
    failures.push(`前端目录缺失 ${dir}`);
    return;
  }
  // 支持 index-*.js 通配
  const files = fs.readdirSync(dir).filter((f) => {
    const re = new RegExp("^" + fe.file.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*") + "$");
    return re.test(f);
  });
  if (files.length === 0) {
    console.log(`\n=== 前端 bundle ===`);
    console.log(`  ✗ 没找到匹配 ${fe.file} 的文件`);
    fail++;
    failures.push(`前端 bundle 缺失 ${fe.file}`);
    return;
  }
  for (const f of files) {
    const full = path.join(dir, f);
    const buf = fs.readFileSync(full);
    const text = buf.toString("utf8");
    const lines = text.split("\n").length;
    console.log(`\n=== 前端 ${f}  ${buf.length.toLocaleString()} bytes / ${lines} 行 ===`);
    if (fe.note) console.log(`  ℹ ${fe.note}`);
    for (const n of fe.must ?? []) {
      const count = text.split(n).length - 1;
      if (count > 0) {
        pass++;
        console.log(`  ✓ ${JSON.stringify(n)}  出现 ${count} 次`);
      } else {
        fail++;
        failures.push(`前端 ${f} 缺 ${JSON.stringify(n)}`);
        console.log(`  ✗ ${JSON.stringify(n)}  未出现`);
      }
    }
  }
}

console.log(`清单：${MANIFEST}`);
console.log(`根目录：${ROOT}`);

for (const spec of manifest.targets ?? []) checkBinary(spec.file, spec);
if (manifest.frontend) checkFrontend(manifest.frontend);

console.log(`\n${"─".repeat(56)}`);
if (fail === 0) {
  console.log(`全部命中 ✓  （${pass} 项）`);
  process.exit(0);
}
console.log(`✗ ${fail} 项未通过 / ${pass} 项通过`);
console.log("未通过明细：");
for (const f of failures) console.log(`  · ${f}`);
console.log("\n排查建议：");
console.log("  1. 改动是否真的保存了？（重读源文件确认）");
console.log("  2. 构建是否用了 --no-restore 跳过还原，或用了旧的 obj/ 缓存？→ 重新 dotnet build");
console.log("  3. 是不是改了源码但没重新 publish？（dist/publish 是 publish 的产物，不是 build 的）");
process.exit(1);
