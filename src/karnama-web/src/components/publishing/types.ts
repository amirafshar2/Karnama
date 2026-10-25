import type { FileItem } from "@/lib/work-types";

export type PublishStatus = "scheduled" | "due" | "published" | "overdue" | "cancelled";
export type PublishJob = {
  id: string;
  clientId: string;
  clientName: string;
  orderId: string | null;
  orderNumber: number | null;
  platformId: string;
  platformName: string;
  handle: string;
  management: "agencyManaged" | "clientManaged";
  publishAt: string;
  caption: string;
  hashtags: string[];
  files: FileItem[];
  status: PublishStatus;
  publishedAt: string | null;
  postUrl: string | null;
  publishedByClient: boolean;
  version: number;
};

/** Iran has had no DST since 2022: local Tehran time is always +03:30. */
export const TEHRAN = "+03:30";

/** "2026-10-25" + "18:30" → ISO in Tehran time. */
export const atTehran = (day: string, time: string) => `${day}T${time}:00${TEHRAN}`;

/** ISO → {day:"2026-10-25", time:"18:30"} in Tehran time. */
export function splitTehran(iso: string) {
  const d = new Date(new Date(iso).getTime() + 210 * 60_000);
  const s = d.toISOString();
  return { day: s.slice(0, 10), time: s.slice(11, 16) };
}

export const statusTone: Record<PublishStatus, string> = {
  scheduled: "border-border bg-card",
  due: "border-amber-500 bg-amber-50 dark:bg-amber-950/40",
  overdue: "border-destructive bg-destructive/10",
  published: "border-emerald-600/60 bg-emerald-50 dark:bg-emerald-950/30",
  cancelled: "border-dashed opacity-60",
};
