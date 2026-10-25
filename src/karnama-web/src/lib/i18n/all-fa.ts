import { faCore } from "./fa";
import { faAccess } from "./fa-access";
import { faCrud } from "./fa-crud";
import { faM2 } from "./fa-m2";
import { faM2Forms } from "./fa-m2-forms";
import { faRoles } from "./fa-roles";
import { faSettings } from "./fa-settings";

/** Every built-in Persian text, i.e. the full list of editable keys (loaded only by the texts editor). */
export const ALL_FA: Record<string, string> = { ...faCore, ...faAccess, ...faRoles, ...faSettings, ...faCrud, ...faM2, ...faM2Forms };
