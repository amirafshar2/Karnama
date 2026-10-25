import { Languages, LayoutDashboard, Palette, ShieldCheck, UserRound, type LucideIcon } from "lucide-react";
import type { TextKey } from "@/lib/i18n/registry";
import { hasPermission, type Me, type Scope } from "@/lib/types";

export type NavItem = {
  href: string;
  label: TextKey;
  icon: LucideIcon;
  /** Shown if the user has ANY of these (permission, minimum scope). */
  anyOf?: { code: string; scope: Scope }[];
};

/** Menu is built from the user's permissions; new modules add their entries here as they ship. */
export const NAV: NavItem[] = [
  { href: "/dashboard", label: "nav.dashboard", icon: LayoutDashboard },
  { href: "/roles", label: "nav.roles", icon: ShieldCheck, anyOf: [{ code: "roles.manage", scope: "all" }] },
  { href: "/settings/brand", label: "nav.brand", icon: Palette, anyOf: [{ code: "settings.manage", scope: "all" }] },
  {
    href: "/settings/texts",
    label: "nav.texts",
    icon: Languages,
    anyOf: [
      { code: "languages.manage", scope: "all" },
      { code: "content.manage", scope: "all" },
    ],
  },
  { href: "/profile", label: "nav.profile", icon: UserRound },
];

export function navFor(me: Me | null) {
  return NAV.filter((i) => !i.anyOf || i.anyOf.some((p) => hasPermission(me, p.code, p.scope)));
}
