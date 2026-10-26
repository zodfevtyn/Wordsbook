// 解析 Blank 16.txt：按日期分段，拆出荷兰语 / 英文 / 注音。
// 只做提取，不做归并；归并在 build-data.mjs 里做。
import fs from "node:fs";

const SRC = process.argv[2] ?? "D:/xwechat_files/wxid_lkqxejvpquod22_c37a/msg/file/2026-10/Blank 16.txt";
const raw = fs.readFileSync(SRC, "utf8").replace(/\r/g, "");

const MONTH = { Jan: 1, Feb: 2, Mar: 3, Apr: 4, May: 5, Jun: 6, Jul: 7, Aug: 8, Sep: 9, Oct: 10, Nov: 11, Dec: 12 };
const dateA = /^(Mon|Tue|Wed|Thu|Fri|Sat|Sun|Say)\s+(\d{1,2})\s+([A-Z][a-z]{2})$/;   // Wed 12 Aug
const dateB = /^(Mon|Tue|Wed|Thu|Fri|Sat|Sun|Say)\s+([A-Z][a-z]{2})\s+(\d{1,2})$/;   // Fri Aug 14

const iso = (d, m) => `2026-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`;

const sections = [];
let cur = null;
for (const rawLine of raw.split("\n")) {
  const line = rawLine.trim();
  if (!line) continue;

  let mm = line.match(dateA);
  if (mm) { cur = { date: iso(+mm[2], MONTH[mm[3]]), lines: [] }; sections.push(cur); continue; }
  mm = line.match(dateB);
  if (mm) { cur = { date: iso(+mm[3], MONTH[mm[2]]), lines: [] }; sections.push(cur); continue; }

  if (/^(Dutch|Greeting|Pronunciation)\s*[:：]?/.test(line) && !line.includes(" / ")) continue;
  if (!cur) { cur = { date: "2026-08-12", lines: [] }; sections.push(cur); }
  cur.lines.push(line);
}

// "de vrouw / the woman [‘the(d) flauw’]" → { nl, en, pron }
function split(line) {
  const parts = line.split(/\s+\/\s*|\s*\/\s+/);
  const nl = (parts[0] ?? "").trim();
  const rest = parts.slice(1).join(" / ").trim();
  const pronMatches = [...rest.matchAll(/\[([^\]]+)\]/g)];
  const pron = pronMatches.map(m => m[1].replace(/[‘’'"]/g, "").trim()).filter(Boolean).join("; ");
  const en = rest.replace(/\[[^\]]+\]/g, "").replace(/\s*;\s*$/, "").trim();
  return { nl, en, pron };
}

const out = sections.map(s => ({
  date: s.date,
  lines: s.lines.map(raw => {
    if (!raw.includes("/")) return { raw };
    return { raw, ...split(raw) };
  }),
}));

const target = process.argv[3] ?? "tools/lines.json";
fs.writeFileSync(target, JSON.stringify(out, null, 1));

const total = out.reduce((n, s) => n + s.lines.length, 0);
console.log(`sections=${out.length} lines=${total} -> ${target}`);
for (const s of out) console.log(`  ${s.date}  ${s.lines.length}`);
