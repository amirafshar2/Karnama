export type Scope = "own" | "assigned" | "all";

export type Me = {
  id: string;
  displayName: string;
  phoneMasked: string;
  kind: "staff" | "client";
  isDemo: boolean;
  languageCode: string;
  twoFactorEnabled: boolean;
  roles: string[];
  permissions: Record<string, Scope>;
};

export type Language = {
  code: string;
  name: string;
  direction: "rtl" | "ltr";
  calendar: "persian" | "gregorian" | "hijri";
  digits: "persian" | "latin";
  isDefault: boolean;
};

export type PermissionDefinition = { code: string; group: string; allowedScopes: Scope[] };
export type Grant = { code: string; scope: Scope };

export type RoleListItem = {
  id: string;
  name: string;
  nameValues: Record<string, string>;
  description: string | null;
  isSystem: boolean;
  systemKey: string | null;
  permissionCount: number;
  userCount: number;
  isDeleted: boolean;
  version: number;
};

export type RoleDetail = {
  id: string;
  nameValues: Record<string, string>;
  description: string | null;
  isSystem: boolean;
  systemKey: string | null;
  permissions: Grant[];
  isDeleted: boolean;
  version: number;
};

export type KeysetPage<T> = { items: T[]; nextCursor: string | null };

export type DemoAccount = { key: string; name: string; description: string; icon: string };

export const SCOPE_RANK: Record<Scope, number> = { own: 1, assigned: 2, all: 3 };

export function hasPermission(me: Me | null | undefined, code: string, min: Scope = "own") {
  const s = me?.permissions[code];
  return !!s && SCOPE_RANK[s] >= SCOPE_RANK[min];
}

/** Display group of a permission (mirrors the server catalog's groups). */
const GROUP_OVERRIDES: Record<string, string> = { languages: "settings", content: "settings", roles: "users", influencers: "campaigns" };
export function permissionGroup(code: string) {
  const prefix = code.split(".")[0];
  return GROUP_OVERRIDES[prefix] ?? prefix;
}
