import { faAccess } from "./fa-access";
import { faCore } from "./fa";
import { faRoles } from "./fa-roles";
import { faSettings } from "./fa-settings";

/** Every text key in the product (type only — module dictionaries are loaded by the pages that use them). */
export type TextKey = keyof typeof faCore | keyof typeof faAccess | keyof typeof faRoles | keyof typeof faSettings;
export type Dictionary = Record<TextKey, string>;

const registered: Partial<Dictionary> = { ...faCore };
let version = 0;
const listeners = new Set<() => void>();

/** Called at the top of a page module that needs a module dictionary (it ships in that page's chunk). */
export function registerTexts(dict: Partial<Dictionary>) {
  let changed = false;
  for (const [k, v] of Object.entries(dict)) {
    if (registered[k as TextKey] !== v) {
      registered[k as TextKey] = v;
      changed = true;
    }
  }
  if (changed) {
    version++;
    listeners.forEach((l) => l());
  }
}

export function persianDefault(key: TextKey): string | undefined {
  return registered[key];
}

export const textsRegistry = {
  subscribe: (l: () => void) => (listeners.add(l), () => listeners.delete(l)),
  version: () => version,
};
