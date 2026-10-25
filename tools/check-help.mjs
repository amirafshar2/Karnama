#!/usr/bin/env node
// Every form field needs a one-line plain-language help text (PROMPT2 §13).
// Scans src/karnama-web/src/**/*.tsx for TextField, NumberField, SelectField, TextArea,
// JalaliDateField and Toggle imported from "@/components/app/form" and fails when one has no `help=` prop.
// Opt-out for a genuinely self-explanatory field: put `{/* help-ok: reason */}` on the line right before it.
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const repo = join(dirname(fileURLToPath(import.meta.url)), "..");
const srcRoot = join(repo, "src", "karnama-web", "src");
const FIELDS = new Set(["TextField", "NumberField", "SelectField", "TextArea", "JalaliDateField", "Toggle"]);

function* walk(dir) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === "node_modules" || e.name.startsWith(".")) continue;
    const p = join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (e.name.endsWith(".tsx")) yield p;
  }
}

/** Names imported from the form module (so a local `Toggle` icon variable is not mistaken for the field). */
function formImports(src) {
  const names = new Set();
  for (const m of src.matchAll(/import\s*\{([^}]*)\}\s*from\s*["'](?:@\/components\/app\/form|\.\/form)["']/g))
    for (const part of m[1].split(",")) {
      const [orig, alias] = part.trim().replace(/^type\s+/, "").split(/\s+as\s+/);
      if (FIELDS.has(orig)) names.add(alias ?? orig);
    }
  return names;
}

/** End index of the JSX opening tag starting at `i` (skips over {…} expressions and string literals). */
function tagEnd(src, i) {
  let depth = 0;
  let quote = null;
  for (let j = i; j < src.length; j++) {
    const c = src[j];
    if (quote) {
      if (c === "\\") j++;
      else if (c === quote) quote = null;
      continue;
    }
    if (c === '"' || c === "'" || c === "`") quote = c;
    else if (c === "{") depth++;
    else if (c === "}") depth--;
    else if (c === ">" && depth === 0) return j;
  }
  return src.length;
}

const missing = [];
let total = 0;
let optedOut = 0;
for (const file of walk(srcRoot)) {
  const src = readFileSync(file, "utf8");
  const names = formImports(src);
  if (names.size === 0) continue;
  const lines = src.split("\n");
  const re = new RegExp(`<(${[...names].join("|")})(?=[\\s/>])`, "g");
  for (const m of src.matchAll(re)) {
    const end = tagEnd(src, m.index + m[0].length);
    const attrs = src.slice(m.index + m[0].length, end);
    total++;
    if (/(^|\s)help=/.test(attrs)) continue;
    const line = src.slice(0, m.index).split("\n").length;
    if (/\{\/\*\s*help-ok:\s*\S.*\*\/\}/.test(lines[line - 2] ?? "")) {
      optedOut++;
      continue;
    }
    missing.push(`${relative(repo, file)}:${line}  <${m[1]}> has no help text`);
  }
}

if (missing.length) {
  console.error(`${missing.length} of ${total} form fields have no help text (add help={t("…Help")}):`);
  for (const x of missing) console.error("  " + x);
  process.exit(1);
}
console.log(`check-help: all ${total} fields explained${optedOut ? ` (${optedOut} opted out with help-ok)` : ""}.`);
