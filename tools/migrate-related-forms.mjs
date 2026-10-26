import fs from "node:fs";
import path from "node:path";

const DATA = path.join(process.env.LOCALAPPDATA, "Wordsbook", "wordsbook.json");
const data = JSON.parse(fs.readFileSync(DATA, "utf8"));

const stats = { words: 0, withForms: 0, multi: 0, forms: 0, emptyNature: 0, existing: 0 };
const anomalies = [];

function splitUsage(usage) {
  return usage
    .split("·")
    .map((s) => s.trim())
    .filter(Boolean);
}

for (const word of data.words) {
  stats.words++;

  let rows = [];
  if (Array.isArray(word.relatedForms) && word.relatedForms.length > 0) {
    stats.existing++;
    rows = word.relatedForms.map((f) => ({
      form: String(f.form ?? "").trim(),
      nature: String(f.nature ?? "").trim(),
    }));
  } else {
    const forms = (word.related ?? []).map((f) => String(f).trim()).filter(Boolean);
    const usage = String(word.relatedUsage ?? "").trim();

    if (forms.length === 1) {
      rows = [{ form: forms[0], nature: usage }];
    } else if (forms.length > 1) {
      const parts = splitUsage(usage);
      if (parts.length !== forms.length) {
        anomalies.push(`${word.word}: ${forms.length} forms but ${parts.length} usage parts (${usage})`);
        rows = forms.map((form, i) => ({ form, nature: parts[i] ?? "" }));
      } else {
        rows = forms.map((form, i) => ({ form, nature: parts[i] }));
      }
    }
  }

  const seen = new Set();
  rows = rows.filter((row) => {
    if (!row.form) return false;
    const key = row.form.toLocaleLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });

  word.relatedForms = rows;
  if (rows.length > 0) stats.withForms++;
  if (rows.length > 1) stats.multi++;
  stats.forms += rows.length;
  stats.emptyNature += rows.filter((r) => !r.nature).length;
}

if (anomalies.length > 0) {
  console.error("USAGE/FORM COUNT MISMATCH:");
  for (const a of anomalies) console.error("  " + a);
  process.exit(1);
}

if (stats.words !== 286 || stats.withForms !== 82 || stats.multi !== 19 || stats.forms !== 103) {
  console.error("unexpected stats", stats);
  process.exit(1);
}

const now = new Date();
const pad = (n) => String(n).padStart(2, "0");
const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
const backup = DATA.replace(/\.json$/, `.backup-${stamp}.json`);
fs.copyFileSync(DATA, backup);
if (fs.statSync(backup).size !== fs.statSync(DATA).size) {
  throw new Error("backup size mismatch");
}

const tmp = path.join(path.dirname(DATA), `wordsbook.tmp-${process.pid}.json`);
fs.writeFileSync(tmp, JSON.stringify(data, null, 2) + "\n", "utf8");
fs.renameSync(tmp, DATA);

const check = JSON.parse(fs.readFileSync(DATA, "utf8"));
if (check.words.length !== 286) throw new Error("word count changed");
if (check.words.filter((w) => (w.relatedForms ?? []).length > 0).length !== 82) throw new Error("form word count changed");
if (check.words.reduce((n, w) => n + (w.relatedForms ?? []).length, 0) !== 103) throw new Error("form count changed");

console.log(`backup: ${backup}`);
console.log(`stats: ${JSON.stringify(stats)}`);
for (const word of data.words.filter((w) => (w.relatedForms ?? []).length > 1).slice(0, 5)) {
  console.log(`${word.word}: ${word.relatedForms.map((r) => `${r.form} [${r.nature || "-"}]`).join(" | ")}`);
}
