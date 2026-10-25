import { Camera, Gauge, Inbox, MessagesSquare, BarChart3, CalendarClock, Newspaper, Globe, Trophy, PhoneIncoming, Boxes, Briefcase, CalendarDays, ClipboardList, Clock, KanbanSquare, QrCode, Settings2, UsersRound, Languages, LayoutDashboard, Network, Package, Palette, ShieldCheck, UserRound, Users, type LucideIcon } from "lucide-react";
import type { TextKey } from "@/lib/i18n/registry";
import { hasPermission, type Me, type Scope } from "@/lib/types";

export type NavItem = {
  href: string;
  label: TextKey;
  icon: LucideIcon;
  /** Shown if the user has ANY of these (permission, minimum scope). */
  anyOf?: { code: string; scope: Scope }[];
  /** Also shown to client (portal) users, who hold no staff permissions. */
  clients?: boolean;
};

/** Menu is built from the user's permissions; new modules add their entries here as they ship. */
export const NAV: NavItem[] = [
  { href: "/dashboard", label: "nav.dashboard", icon: LayoutDashboard },
  { href: "/work", label: "nav.work", icon: KanbanSquare, anyOf: [{ code: "work.view", scope: "assigned" }] },
  { href: "/orders", label: "nav.orders", icon: ClipboardList, anyOf: [{ code: "orders.view", scope: "assigned" }] },
  { href: "/publishing", label: "nav.publishing", icon: CalendarClock, anyOf: [{ code: "orders.manage", scope: "all" }] },
  { href: "/crm", label: "nav.crm", icon: PhoneIncoming, anyOf: [{ code: "crm.leads", scope: "own" }] },
  { href: "/attendance", label: "nav.attendance", icon: Clock, anyOf: [{ code: "attendance.self", scope: "own" }] },
  { href: "/assets", label: "nav.assets", icon: Camera, anyOf: [{ code: "assets.view", scope: "own" }] },
  { href: "/chat", label: "nav.chat", icon: MessagesSquare, anyOf: [{ code: "chat.use", scope: "own" }, { code: "chat.groups.manage", scope: "all" }], clients: true },
  { href: "/attendance/calendar", label: "nav.calendar", icon: CalendarDays, anyOf: [{ code: "attendance.self", scope: "own" }] },
  { href: "/attendance/live", label: "nav.live", icon: UsersRound, anyOf: [{ code: "attendance.view", scope: "all" }] },
  { href: "/reports", label: "nav.reports", icon: Gauge, anyOf: [{ code: "reports.view", scope: "own" }, { code: "scores.view", scope: "own" }] },
  { href: "/attendance/report", label: "nav.attReport", icon: BarChart3, anyOf: [{ code: "attendance.view", scope: "all" }] },
  { href: "/content/blog", label: "nav.blog", icon: Newspaper, anyOf: [{ code: "blog.write", scope: "own" }, { code: "blog.publish", scope: "all" }] },
  { href: "/media", label: "nav.media", icon: Inbox, anyOf: [{ code: "media.inbox", scope: "all" }] },
  { href: "/clients", label: "nav.clients", icon: Briefcase, anyOf: [{ code: "clients.view", scope: "assigned" }] },
  { href: "/packages", label: "nav.packages", icon: Package, anyOf: [{ code: "packages.view", scope: "all" }] },
  { href: "/users", label: "nav.users", icon: Users, anyOf: [{ code: "users.manage", scope: "all" }] },
  { href: "/services", label: "nav.services", icon: Boxes, anyOf: [{ code: "services.manage", scope: "all" }] },
  { href: "/roles", label: "nav.roles", icon: ShieldCheck, anyOf: [{ code: "roles.manage", scope: "all" }] },
  { href: "/settings/platforms", label: "nav.platforms", icon: Network, anyOf: [{ code: "settings.manage", scope: "all" }] },
  { href: "/content/home", label: "nav.site", icon: Globe, anyOf: [{ code: "content.manage", scope: "all" }] },
  { href: "/content/cases", label: "nav.cases", icon: Trophy, anyOf: [{ code: "content.manage", scope: "all" }] },
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
  return NAV.filter((i) => !i.anyOf || i.anyOf.some((p) => hasPermission(me, p.code, p.scope)) || (i.clients && me?.kind === "client"));
}
