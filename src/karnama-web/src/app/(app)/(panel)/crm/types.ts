export type StageKind = "open" | "won" | "lost";
export type Stage = { id: string; name: string; kind: StageKind; sortOrder: number; isActive: boolean };
export type Source = { id: string; name: string; sortOrder: number; isActive: boolean };
export type LeadItem = {
  id: string;
  name: string;
  business: string | null;
  phone: string | null;
  city: string | null;
  instagram: string | null;
  sourceId: string | null;
  sourceName: string | null;
  stageId: string;
  ownerId: string | null;
  ownerName: string | null;
  tags: string[];
  budget: number | null;
  nextFollowUpAt: string | null;
  lastActivityAt: string;
  createdAt: string;
  clientId: string | null;
  version: number;
};
export type Activity = {
  id: number;
  kind: "created" | "call" | "message" | "meeting" | "email" | "note" | "stageChanged" | "assigned" | "converted" | "inbound";
  actorName: string | null;
  direction: "outgoing" | "incoming" | null;
  durationSeconds: number | null;
  outcome: "noAnswer" | "interested" | "later" | "rejected" | "done" | null;
  channel: "telegram" | "whatsApp" | "sms" | "bale" | "instagram" | null;
  note: string | null;
  from: string | null;
  to: string | null;
  at: string;
};
export type LeadDetail = { lead: LeadItem; industry: string | null; message: string | null; origin: string | null; note: string | null; lostReason: string | null; activities: Activity[] };
export type Marketer = { id: string; name: string; openLeads: number };
export type CrmSettings = { autoAssign: boolean; commissionEnabled: boolean; commissionPercent: number };
export type CrmReport = {
  from: string;
  to: string;
  totalLeads: number;
  marketers: { id: string; name: string; leads: number; calls: number; callMinutes: number; won: number; lost: number; sales: number; commission: number | null }[];
  stages: { stageId: string; name: string; reached: number; percent: number }[];
  sources: { sourceId: string | null; name: string; leads: number; won: number; rate: number }[];
};

/** Iran has no DST since 1401: +03:30 all year. */
export const tehran = (dayIso: string, time: string) => `${dayIso.slice(0, 10)}T${time}:00+03:30`;
