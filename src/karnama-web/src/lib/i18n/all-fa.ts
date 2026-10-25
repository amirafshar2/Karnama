import { faCore } from "./fa";
import { faAuth, faDashboard, faProfile } from "./fa-pages";
import { faAccess } from "./fa-access";
import { faCrud } from "./fa-crud";
import { faM2 } from "./fa-m2";
import { faM2Forms } from "./fa-m2-forms";
import { faM3 } from "./fa-m3";
import { faM4 } from "./fa-m4";
import { faPortal } from "./fa-portal";
import { faM5 } from "./fa-m5";
import { faRoles } from "./fa-roles";
import { faSettings } from "./fa-settings";

/** Every built-in Persian text, i.e. the full list of editable keys (loaded only by the texts editor). */
export const ALL_FA: Record<string, string> = { ...faCore, ...faAuth, ...faDashboard, ...faProfile, ...faAccess, ...faRoles, ...faSettings, ...faCrud, ...faM2, ...faM2Forms, ...faM3, ...faM4, ...faPortal, ...faM5 };
