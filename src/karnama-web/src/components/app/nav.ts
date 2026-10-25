import { Boxes, Briefcase, ClipboardList, Clock, KanbanSquare, QrCode, Settings2, UsersRound, Languages, LayoutDashboard, Network, Package, Palette, ShieldCheck, UserRound, Users, type LucideIcon } from "lucide-react";
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
  { href: "/work", label: "nav.work", icon: KanbanSquare, anyOf: [{ code: "work.view", scope: "assigned" }] },
  { href: "/orders", label: "nav.orders", icon: ClipboardList, anyOf: [{ code: "orders.view", scope: "assigned" }] },
  { href: "/attendance", label: "nav.attendance", icon: Clock, anyOf: [{ code: "attendance.self", scope: "own" }] },
  { href: "/attendance/live", label: "nav.live", icon: UsersRound, anyOf: [{ code: "attendance.view", scope: "all" }] },
  { href: "/clients", label: "nav.clients", icon: Briefcase, anyOf: [{ code: "clients.view", scope: "assigned" }] },
  { href: "/packages", label: "nav.packages", icon: Package, anyOf: [{ code: "packages.view", scope: "all" }] },
  { href: "/users", label: "nav.users", icon: Users, anyOf: [{ code: "users.manage", scope: "all" }] },
  { href: "/services", label: "nav.services", icon: Boxes, anyOf: [{ code: "services.manage", scope: "all" }] },
  { href: "/roles", label: "nav.roles", icon: ShieldCheck, anyOf: [{ code: "roles.manage", scope: "all" }] },
  { href: "/settings/platforms", label: "nav.platforms", icon: Network, anyOf: [{ code: "settings.manage", scope: "all" }] },
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
  { href: "/attendance/setup", label: "nav.attSetup", icon: Settings2, anyOf: [{ code: "attendance.policies.manage", scope: "all" }] },
  { href: "/attendance/kiosk", label: "nav.kiosk", icon: QrCode, anyOf: [{ code: "attendance.kiosk", scope: "all" }] },
  { href: "/profile", label: "nav.profile", icon: UserRound },
];

export function navFor(me: Me | null) {
  return NAV.filter((i) => !i.anyOf || i.anyOf.some((p) => hasPermission(me, p.code, p.scope)));
}
