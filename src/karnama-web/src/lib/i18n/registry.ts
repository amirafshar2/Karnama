import type { faAccess } from "./fa-access";
import type { faCrud } from "./fa-crud";
import type { faM2 } from "./fa-m2";
import type { faM2Forms } from "./fa-m2-forms";
import type { faM3 } from "./fa-m3";
import type { faM4 } from "./fa-m4";
import type { faPortal } from "./fa-portal";
import type { faM5 } from "./fa-m5";
import type { faM6 } from "./fa-m6";
import type { faSite } from "./fa-site";
import type { faBlog } from "./fa-blog";
import type { faSiteAdmin } from "./fa-siteadmin";
import type { faN4 } from "./fa-n4";
import type { faN5 } from "./fa-n5";
import type { faN6 } from "./fa-n6";
import type { faN7 } from "./fa-n7";
import type { faN8 } from "./fa-n8";
import type { faN3 } from "./fa-n3";
import type { faN12 } from "./fa-n12";
import type { faN2 } from "./fa-n2";
import type { faN11 } from "./fa-n11";
import { faCore } from "./fa";
import type { faAuth, faDashboard, faProfile } from "./fa-pages";
import type { faRoles } from "./fa-roles";
import type { faSettings } from "./fa-settings";
import type { faAppDl } from "./fa-appdl";

/** Every text key in the product (type only — module dictionaries are loaded by the pages that use them). */
export type TextKey =
  | keyof typeof faCore
  | keyof typeof faAppDl
  | keyof typeof faAuth
  | keyof typeof faDashboard
  | keyof typeof faProfile
  | keyof typeof faAccess
  | keyof typeof faRoles
  | keyof typeof faSettings
  | keyof typeof faCrud
  | keyof typeof faM2
  | keyof typeof faM2Forms
  | keyof typeof faM3
  | keyof typeof faM4
  | keyof typeof faPortal
  | keyof typeof faM5
  | keyof typeof faM6
  | keyof typeof faSite
  | keyof typeof faBlog
  | keyof typeof faSiteAdmin
  | keyof typeof faN4
  | keyof typeof faN5
  | keyof typeof faN6
  | keyof typeof faN7
  | keyof typeof faN8
  | keyof typeof faN3
  | keyof typeof faN12
  | keyof typeof faN2
  | keyof typeof faN11;
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
