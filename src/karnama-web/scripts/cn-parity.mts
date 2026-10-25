// Checks that src/lib/utils.ts cn() resolves conflicts like tailwind-merge for the cases this app uses.
// Usage (from src/karnama-web):  npm i --no-save tailwind-merge tsx && npx tsx scripts/cn-parity.mts   (tailwind-merge is fetched only for this check)
import { twMerge } from "tailwind-merge";
import { cn } from "../src/lib/utils.ts";

const cases = [
  ["h-9 px-4 py-2", "h-12 w-full"],
  ["inline-flex h-9 items-center justify-center gap-2 rounded-md px-4 text-sm", "h-auto min-h-14 justify-start gap-3 px-3 py-2 text-start whitespace-normal"],
  ["flex h-9 w-full rounded-md border px-3 py-1 text-base md:text-sm", "h-12 text-end text-lg tracking-wider"],
  ["relative flex h-9 w-9 items-center border-y border-e text-sm", "size-12 text-lg"],
  ["fixed z-50 flex flex-col gap-4 bg-background shadow-lg inset-y-0 right-0 h-full w-3/4 border-l sm:max-w-sm", "flex flex-col gap-0 p-0 w-full sm:max-w-xl"],
  ["fixed z-50 inset-x-0 bottom-0 h-auto border-t", "h-[92dvh] rounded-t-2xl"],
  ["text-muted-foreground text-sm", "text-destructive"],
  ["bg-primary text-primary-foreground hover:bg-primary/90", "bg-destructive hover:bg-destructive/80"],
  ["p-4", "px-2"],
  ["px-4", "ps-2"],
  ["rounded-md", "rounded-t-2xl"],
  ["border border-input", "border-destructive"],
  ["font-medium", "font-bold"],
  ["hidden md:block", "md:flex"],
  ["text-sm data-[state=open]:text-sm", "data-[state=open]:text-lg"],
];

let failed = 0;
const norm = (s) => s.split(" ").sort().join(" ");
for (const [a, b] of cases) {
  const mine = cn(a, b);
  const ref = twMerge(a, b);
  const ok = norm(mine) === norm(ref);
  if (!ok) failed++;
  console.log(ok ? "OK  " : "DIFF", `cn("${a}", "${b}")\n      mine: ${mine}\n      ref:  ${ref}`);
}
process.exit(failed ? 1 : 0);
